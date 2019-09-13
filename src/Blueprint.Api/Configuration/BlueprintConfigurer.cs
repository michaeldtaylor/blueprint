﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using Blueprint.Api.Authorisation;
using Blueprint.Api.Errors;
using Blueprint.Api.Formatters;
using Blueprint.Api.Middleware;
using Blueprint.Core;
using Blueprint.Core.Caching;
using Blueprint.Core.Errors;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Api.Configuration
{
    public class BlueprintConfigurer
    {
        private readonly BlueprintApiOptions options;

        private readonly Dictionary<MiddlewareStage, List<Type>> middlewareStages = new Dictionary<MiddlewareStage, List<Type>>();

        public BlueprintConfigurer(IServiceCollection services, BlueprintApiOptions options = null)
        {
            Services = services;

            this.options = options ?? new BlueprintApiOptions();
        }

        public IServiceCollection Services { get; }

        public BlueprintConfigurer Settings(Action<BlueprintSettingsConfigurer> configurer)
        {
            Guard.NotNull(nameof(configurer), configurer);

            configurer(new BlueprintSettingsConfigurer(options));

            return this;
        }

        public BlueprintConfigurer Middlewares(Action<BlueprintMiddlewareConfigurer> configurer)
        {
            Guard.NotNull(nameof(configurer), configurer);

            configurer(new BlueprintMiddlewareConfigurer(this));

            return this;
        }

        public BlueprintConfigurer BackgroundTasks(Action<BlueprintBackgroundTasksConfigurer> configurer)
        {
            Guard.NotNull(nameof(configurer), configurer);

            configurer(new BlueprintBackgroundTasksConfigurer(this));

            return this;
        }

        public void Build()
        {
            if (options.AppName == null)
            {
                throw new InvalidOperationException("An app name MUST be set");
            }

            ComposeMiddlewareBuilders();

            Services.AddSingleton(options);
            Services.AddSingleton(options.Model);
            Services.AddSingleton<IApiOperationExecutor>(s => new ApiOperationExecutorBuilder().Build(options, s));

            // Logging only?
            Services.AddScoped<IErrorLogger, ErrorLogger>();

            // Auth only?
            Services.AddScoped<IApiAuthoriser, MustBeAuthenticatedApiAuthoriser>();
            Services.AddScoped<IApiAuthoriserAggregator, ApiAuthoriserAggregator>();

            Services.AddScoped<ITypeFormatter, JsonTypeFormatter>();
            Services.AddScoped<IResourceLinkGenerator, EntityOperationResourceLinkGenerator>();

            Services.AddSingleton<ICache, Cache>();
            Services.AddSingleton(MemoryCache.Default);
            Services.AddSingleton<IExceptionFilter, BasicExceptionFilter>();

            Services.AddTransient<IInstanceFrameProvider, DefaultInstanceFrameProvider>();

            AddApiOperationHandlers();
        }

        public void AddMiddlewareBuilderToStage<T>(MiddlewareStage middlewareStage) where T : IMiddlewareBuilder
        {
            if (middlewareStages.TryGetValue(middlewareStage, out var middlewareTypes))
            {
                middlewareTypes.Add(typeof(T));
            }
            else
            {
                middlewareStages.Add(middlewareStage, new List<Type> { typeof(T) });
            }
        }

        private void ComposeMiddlewareBuilders()
        {
            if (options.Middlewares.Any())
            {
                return;
            }

            AddMiddlewareBuilders(MiddlewareStage.OperationChecks);
            AddMiddlewareBuilders(MiddlewareStage.PreExecute);

            // Execute
            options.UseMiddlewareBuilder<OperationExecutorMiddlewareBuilder>();
            options.UseMiddlewareBuilder<FormatterMiddlewareBuilder>();

            AddMiddlewareBuilders(MiddlewareStage.PostExecute);
        }

        private void AddMiddlewareBuilders(MiddlewareStage middlewareStage)
        {
            if (!middlewareStages.TryGetValue(middlewareStage, out var middlewareTypes))
            {
                return;
            }

            foreach (var middlewareType in middlewareTypes)
            {
                options.Middlewares.Add(middlewareType);
            }
        }

        private void AddApiOperationHandlers()
        {
            var missingApiOperationHandlers = new List<ApiOperationDescriptor>();

            foreach (var operation in options.Model.Operations)
            {
                var apiOperationHandlerType = typeof(IApiOperationHandler<>).MakeGenericType(operation.OperationType);
                var apiOperationHandler = FindApiOperationHandler(operation, apiOperationHandlerType);

                if (apiOperationHandler == null)
                {
                    missingApiOperationHandlers.Add(operation);
                }

                Services.AddScoped(apiOperationHandlerType, apiOperationHandler);
            }

            if (missingApiOperationHandlers.Any())
            {
                throw new MissingApiOperationHandlerException(missingApiOperationHandlers.ToArray());
            }
        }

        private static Type FindApiOperationHandler(ApiOperationDescriptor apiOperationDescriptor, Type apiOperationHandlerType)
        {
            return apiOperationDescriptor.OperationType.Assembly.GetExportedTypes().SingleOrDefault(apiOperationHandlerType.IsAssignableFrom);
        }
    }
}
