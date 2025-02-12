﻿using System;
using System.Threading.Tasks;
using Blueprint.Testing;
using FluentAssertions;
using NUnit.Framework;
using Snapper.Attributes;

namespace Blueprint.Tests.Core
{
    [UpdateSnapshots]
    public class Given_PolymorphicOperationDeclaration
    {
        [Test]
        public async Task When_Interface_Operation_Registered_RequiresReturnValue_False_Concrete_Operation_Can_Be_Executed()
        {
            // Arrange
            var executor = TestApiOperationExecutor
                .CreateStandalone(o => o
                    .WithHandler(new TestApiOperationHandler<OperationImpl>("ignored"))
                    .WithOperation<IOperationInterface>(c => c.RequiresReturnValue = false));

            // Act
            var result = await executor.ExecuteWithNewScopeAsync(new OperationImpl());

            // Assert
            result.Should().BeOfType<OkResult>();
        }

        [Test]
        public void When_Interface_Operation_Registered_RequiresReturnValue_True_Exception_On_Build()
        {
            // Arrange
            Action tryBuildExecutor = () => TestApiOperationExecutor
                .CreateStandalone(o => o
                    .WithHandler(new TestApiOperationHandler<OperationImpl>("ignored"))
                    .WithOperation<IOperationInterface>(c => c.RequiresReturnValue = true));

            // Act
            tryBuildExecutor.Should().ThrowExactly<InvalidOperationException>()
                .WithMessage(@"Unable to build an executor for the operation Blueprint.Tests.Core.Given_PolymorphicOperationDeclaration+IOperationInterface because the single handler registered, IoC as Blueprint.Tests.TestApiOperationHandler`1[Blueprint.Tests.Core.Given_PolymorphicOperationDeclaration+OperationImpl], did not return a variable but the operation has RequiresReturnValue set to true. 

This can happen if an the only registered handler for an operation is one that is NOT of the same type (for example a handler IApiOperationHandler<ConcreteClass> for the operation IOperationInterface) where it cannot be guaranteed that the handler will be executed.");
        }

        [Test]
        public async Task When_multiple_child_operations_finds_correct_one()
        {
            // Arrange
            var baseHandler = new TestApiOperationHandler<OperationBase>("ignored");
            var child1Handler = new TestApiOperationHandler<OperationChild1>("ignored");
            var child2Handler = new TestApiOperationHandler<OperationChild2>("ignored");

            var executor = TestApiOperationExecutor
                .CreateStandalone(o => o
                        .WithHandler(baseHandler)
                        .WithHandler(child1Handler)
                        .WithHandler(child2Handler)
                        .WithOperation<OperationBase>(c => c.RequiresReturnValue = false)
                        .WithOperation<OperationChild1>(c => c.RequiresReturnValue = false)
                        .WithOperation<OperationChild2>(c => c.RequiresReturnValue = false));

            // Act
            var result = await executor.ExecuteWithNewScopeAsync(new OperationChild2());

            // Assert
            result.Should().BeOfType<NoResultOperationResult>();

            baseHandler.WasCalled.Should().BeTrue();
            child2Handler.WasCalled.Should().BeTrue();

            child1Handler.WasCalled.Should().BeFalse();
        }

        // This test cares that a handler of OperationBase does NOT have additional casts or
        // if checks for the type as OperationChild2 will ALWAYS match
        [Test]
        public void When_multiple_child_operations_does_not_cast_or_wrap_in_if_when_handling_parent()
        {
            // Arrange
            var baseHandler = new TestApiOperationHandler<OperationBase>("ignored");
            var child1Handler = new TestApiOperationHandler<OperationChild1>("ignored");
            var child2Handler = new TestApiOperationHandler<OperationChild2>("ignored");

            var executor = TestApiOperationExecutor
                .CreateStandalone(o => o
                    .WithHandler(baseHandler)
                    .WithHandler(child1Handler)
                    .WithHandler(child2Handler)
                    .WithOperation<OperationBase>(c => c.RequiresReturnValue = false)
                    .WithOperation<OperationChild1>(c => c.RequiresReturnValue = false)
                    .WithOperation<OperationChild2>(c => c.RequiresReturnValue = false));

            // Act
            var result = executor.WhatCodeDidIGenerateFor<OperationChild2>();

            // Assert
            result.Should().Be(@"// <auto-generated />
// OperationChild2ExecutorPipeline

using Blueprint;
using Blueprint.Errors;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Blueprint.Tests.Core
{
    public class OperationChild2ExecutorPipeline : Blueprint.IOperationExecutorPipeline
    {
        private static readonly System.Action<Microsoft.Extensions.Logging.ILogger, string, string, System.Exception> _ApiOperationHandlerExecuting = Microsoft.Extensions.Logging.LoggerMessage.Define<System.String, System.String>(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(2, ""ApiOperationHandlerExecuting""), ""Executing API operation {OperationType} with handler {HandlerType}"");

        private readonly Microsoft.Extensions.Logging.ILogger _logger;
        private readonly Blueprint.IApiOperationHandler<Blueprint.Tests.Core.Given_PolymorphicOperationDeclaration.OperationBase> _operationBaseIApiOperationHandler;
        private readonly Blueprint.IApiOperationHandler<Blueprint.Tests.Core.Given_PolymorphicOperationDeclaration.OperationChild2> _operationChild2IApiOperationHandler;
        private readonly Blueprint.Errors.IErrorLogger _errorLogger;

        public OperationChild2ExecutorPipeline(Microsoft.Extensions.Logging.ILoggerFactory loggerFactory, Blueprint.IApiOperationHandler<Blueprint.Tests.Core.Given_PolymorphicOperationDeclaration.OperationBase> operationBaseIApiOperationHandler, Blueprint.IApiOperationHandler<Blueprint.Tests.Core.Given_PolymorphicOperationDeclaration.OperationChild2> operationChild2IApiOperationHandler, Blueprint.Errors.IErrorLogger errorLogger)
        {
            _logger = loggerFactory.CreateLogger(""OperationChild2ExecutorPipeline"");
            _operationBaseIApiOperationHandler = operationBaseIApiOperationHandler;
            _operationChild2IApiOperationHandler = operationChild2IApiOperationHandler;
            _errorLogger = errorLogger;
        }

        public async System.Threading.Tasks.Task<Blueprint.OperationResult> ExecuteAsync(Blueprint.ApiOperationContext context)
        {
            var operationChild2 = (Blueprint.Tests.Core.Given_PolymorphicOperationDeclaration.OperationChild2) context.Operation;
            try
            {

                // OperationExecutorMiddlewareBuilder
                using var apmSpanOfHandler = context.ApmSpan.StartSpan(""internal"", ""Handler"", ""exec"");
                _ApiOperationHandlerExecuting(_logger, ""OperationChild2"", _operationBaseIApiOperationHandler.GetType().Name, null);
                await _operationBaseIApiOperationHandler.Handle(operationChild2, context);
                _ApiOperationHandlerExecuting(_logger, ""OperationChild2"", _operationChild2IApiOperationHandler.GetType().Name, null);
                await _operationChild2IApiOperationHandler.Handle(operationChild2, context);
                apmSpanOfHandler.Dispose();

                // ReturnFrameMiddlewareBuilder
                return Blueprint.NoResultOperationResult.Instance;
            }
            catch (System.Exception e)
            {
                var userAuthorisationContext = context.UserAuthorisationContext;
                var identifier = new Blueprint.Authorisation.UserExceptionIdentifier(userAuthorisationContext);

                userAuthorisationContext?.PopulateMetadata((k, v) => e.Data[k] = v?.ToString());

                var result_of_LogAsync = await _errorLogger.LogAsync(e, null, identifier);

                context.ApmSpan?.RecordException(e);
                return new Blueprint.UnhandledExceptionOperationResult(e);
            }
        }

        public async System.Threading.Tasks.Task<Blueprint.OperationResult> ExecuteNestedAsync(Blueprint.ApiOperationContext context)
        {
            var operationChild2 = (Blueprint.Tests.Core.Given_PolymorphicOperationDeclaration.OperationChild2) context.Operation;
            try
            {

                // OperationExecutorMiddlewareBuilder
                using var apmSpanOfHandler = context.ApmSpan.StartSpan(""internal"", ""Handler"", ""exec"");
                _ApiOperationHandlerExecuting(_logger, ""OperationChild2"", _operationBaseIApiOperationHandler.GetType().Name, null);
                await _operationBaseIApiOperationHandler.Handle(operationChild2, context);
                _ApiOperationHandlerExecuting(_logger, ""OperationChild2"", _operationChild2IApiOperationHandler.GetType().Name, null);
                await _operationChild2IApiOperationHandler.Handle(operationChild2, context);
                apmSpanOfHandler.Dispose();

                // ReturnFrameMiddlewareBuilder
                return Blueprint.NoResultOperationResult.Instance;
            }
            catch (System.Exception e)
            {
                var userAuthorisationContext = context.UserAuthorisationContext;
                var identifier = new Blueprint.Authorisation.UserExceptionIdentifier(userAuthorisationContext);

                userAuthorisationContext?.PopulateMetadata((k, v) => e.Data[k] = v?.ToString());

                var result_of_LogAsync = await _errorLogger.LogAsync(e, null, identifier);

                context.ApmSpan?.RecordException(e);
                return new Blueprint.UnhandledExceptionOperationResult(e);
            }
        }
    }
}
");
        }

        public class OperationBase {}
        public class OperationChild1 : OperationBase {}
        public class OperationChild2 : OperationBase {}

        public interface IOperationInterface {}

        public class OperationImpl : IOperationInterface
        {
        }
    }
}
