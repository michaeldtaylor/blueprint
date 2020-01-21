﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Blueprint.Core.Apm;
using Blueprint.Core.Errors;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blueprint.Core.Tasks
{
    /// <summary>
    /// Resolves an appropriate task handler and allows it to perform the required action for the task.
    /// </summary>
    public class TaskExecutor
    {
        private static readonly MethodInfo InvokeTaskHandlerMethod = typeof(TaskExecutor)
            .GetMethod(nameof(InvokeTaskHandlerAsync), BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly IServiceProvider serviceProvider;
        private readonly IErrorLogger errorLogger;
        private readonly IApmTool apmTool;
        private readonly ILogger<TaskExecutor> logger;
        private readonly IOptions<TaskOptions> options;

        /// <summary>
        /// Instantiates a new instance of the TaskExecutor class.
        /// </summary>
        /// <param name="serviceProvider">The parent container.</param>
        /// <param name="errorLogger">Error logger to track thrown exceptions.</param>
        /// <param name="apmTool">APM operation tracker to track individual task executions.</param>
        /// <param name="logger">Logger to use.</param>
        /// <param name="options">The options for this executor.</param>
        public TaskExecutor(
            IServiceProvider serviceProvider,
            IErrorLogger errorLogger,
            IApmTool apmTool,
            ILogger<TaskExecutor> logger,
            IOptions<TaskOptions> options)
        {
            Guard.NotNull(nameof(serviceProvider), serviceProvider);
            Guard.NotNull(nameof(errorLogger), errorLogger);
            Guard.NotNull(nameof(apmTool), apmTool);
            Guard.NotNull(nameof(logger), logger);
            Guard.NotNull(nameof(options), options);

            this.serviceProvider = serviceProvider;
            this.errorLogger = errorLogger;
            this.apmTool = apmTool;
            this.logger = logger;
            this.options = options;
        }

        /// <summary>
        /// Resolves a task handler for the given command context and, if found, hands off
        /// execution to the command handler.
        /// </summary>
        /// <param name="task">The task to be executed.</param>
        /// <param name="context">The Hangfire context.</param>
        /// <returns>A <see cref="Task" /> representing the execution of the given task.</returns>
        [DisplayName("{0}")]
        public async Task Execute(BackgroundTask task, PerformContext context)
        {
            Guard.NotNull(nameof(task), task);

            await (Task)InvokeTaskHandlerMethod
                .MakeGenericMethod(task.GetType())
                .Invoke(this, new object[] {task, context});
        }

        private static string GetOperationName<TTask>(TTask backgroundTask) where TTask : BackgroundTask
        {
            var categorisedTask = backgroundTask as IHaveTaskCategory;
            var taskType = backgroundTask.GetType();

            return categorisedTask != null ? taskType.Name + "-" + categorisedTask.Category : taskType.Name;
        }

        /// <summary>
        /// Gets the maximum number of attempts allowed, which is the minimum <see cref="AutomaticRetryAttribute.Attempts" />
        /// of all registered filters of type <see cref="AutomaticRetryAttribute"/>.
        /// </summary>
        /// <returns>Maximum number of retry attempts allowed.</returns>
        private static int GetMaxAttempts()
        {
            int? attempts = null;

            foreach (var att in GlobalJobFilters.Filters.OfType<AutomaticRetryAttribute>())
            {
                if (att.Attempts < (attempts ?? int.MaxValue))
                {
                    attempts = att.Attempts;
                }
            }

            return attempts ?? 0;
        }

        private async Task InvokeTaskHandlerAsync<TTask>(TTask backgroundTask, PerformContext context) where TTask : BackgroundTask
        {
            Guard.NotNull(nameof(backgroundTask), backgroundTask);
            Guard.NotNull(nameof(context), context);

            var typeName = backgroundTask.GetType().Name;

            var activity = new Activity("Task_In")
                .SetParentId(backgroundTask.Metadata.RequestId)
                .AddTag("JobId", context.BackgroundJob.Id)
                .AddTag("TaskType", typeName);

            if (backgroundTask.Metadata.RequestBaggage != null)
            {
                foreach (var pair in backgroundTask.Metadata.RequestBaggage)
                {
                    activity.AddBaggage(pair.Key, pair.Value);
                }
            }

            try
            {
                activity.Start();

                using (logger.BeginScope(new {JobId = context.BackgroundJob.Id}))
                using (var nestedContainer = serviceProvider.CreateScope())
                {
                    await apmTool.InvokeAsync(GetOperationName(backgroundTask), async () =>
                    {
                        var handler = nestedContainer.ServiceProvider.GetService<IBackgroundTaskHandler<TTask>>();

                        if (handler == null)
                        {
                            throw new NoTaskHandlerFoundException(
                                $"No task handler found for type '{typeName}'.");
                        }

                        var enableConfigKey = $"Task.{typeName}.Enabled";

                        if (options.Value.DisabledSchedulers.Contains(typeName))
                        {
                            logger.LogWarning(
                                "Task disabled in configuration. task_type={0} handler_type={1}",
                                typeName,
                                handler.GetType().Name);

                            return;
                        }

                        if (logger.IsEnabled(LogLevel.Trace))
                        {
                            logger.LogTrace(
                                "Executing task in new nested container context. task_type={0} handler={1}",
                                backgroundTask.GetType().Name,
                                handler.GetType().Name);
                        }

                        var contextProvider = nestedContainer.ServiceProvider.GetRequiredService<IBackgroundTaskContextProvider>();
                        var contextKey = typeName;
                        var backgroundContext = new BackgroundTaskContext(contextKey, contextProvider);

                        // 1. Handle the actual execution of the task
                        await handler.HandleAsync(backgroundTask, backgroundContext);

                        // 2. Allow any further processing to happen (i.e. may be to save changes in a UoW)
                        var postProcessor = nestedContainer.ServiceProvider.GetService<IBackgroundTaskExecutionPostProcessor>();

                        if (postProcessor != null)
                        {
                            await postProcessor.PostProcessAsync(backgroundTask);
                        }

                        // 3. Save any context data that may have changed
                        await backgroundContext.SaveAsync();

                        // 4. Schedule any new tasks that have been added by the task
                        await nestedContainer.ServiceProvider.GetRequiredService<IBackgroundTaskScheduler>().RunNowAsync();
                    });
                }
            }
            catch (Exception e)
            {
                if (errorLogger.ShouldIgnore(e))
                {
                    return;
                }

                // If this was not the last attempt then we will _not_ attempt to record this exception
                // but will instead just throw to retry. This is designed to reduce intermittent noise
                // of transient errors.
                var attempt = context.GetJobParameter<int?>("RetryCount");

                if (attempt != null && attempt < GetMaxAttempts())
                {
                    throw;
                }

                e.Data["RetryCount"] = attempt?.ToString();
                e.Data["HangfireJobId"] = context.BackgroundJob.Id;

                errorLogger.Log(e);

                throw;
            }
            finally
            {
                activity.Stop();
            }
        }
    }
}
