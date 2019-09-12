﻿using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Blueprint.Api;
using Blueprint.Api.Middleware;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using StructureMap;

namespace Blueprint.Tests.Api.Validator_Middleware
{
    public class Given_ValidationMiddleware
    {
        public class EmptyOperation : IApiOperation
        {
        }

        public class HasRequiredPropertyOperation : IApiOperation
        {
            [Required]
            public object TheProperty { get; set; }
        }

        [Test]
        public async Task When_Empty_Operation_Then_Result_Executed()
        {
            // Arrange
            var toReturn = 12345;

            // Act
            var result = await Execute(new EmptyOperation(), toReturn);

            // Assert
            result.Result.ShouldBeOfType<OkResult>();
            ((OkResult)result.Result).Content.ShouldBe(toReturn);
            result.Handler.WasCalled.ShouldBeTrue();
        }

        [Test]
        public async Task When_Operation_Passes_Validation_Then_Result_Executed()
        {
            // Arrange
            var toReturn = 12345;

            // Act
            var result = await Execute(new HasRequiredPropertyOperation { TheProperty = "something not null"}, toReturn);

            // Assert
            result.Result.ShouldBeOfType<OkResult>();
            ((OkResult)result.Result).Content.ShouldBe(toReturn);
            result.Handler.WasCalled.ShouldBeTrue();
        }

        [Test]
        public async Task When_Operation_Does_Not_Pass_Validation_Then_Handler_Not_Executed()
        {
            // Arrange
            var toReturn = 12345;

            // Act
            var result = await Execute(new HasRequiredPropertyOperation { TheProperty = null }, toReturn);

            // Assert
            result.Handler.WasCalled.ShouldBeFalse();
        }

        [Test]
        public async Task When_Operation_Does_Not_Pass_Validation_Then_ValidationResult_Returned()
        {
            // Arrange
            var toReturn = 12345;

            // Act
            var result = await Execute(new HasRequiredPropertyOperation { TheProperty = null }, toReturn);

            // Assert
            result.Result.ShouldBeOfType<ValidationFailedResult>();
            ((ValidationFailedResult)result.Result).Content.Errors.ShouldContainKey(nameof(HasRequiredPropertyOperation.TheProperty));
        }

        private async Task<(OperationResult Result, TestApiOperationHandler<T> Handler)> Execute<T>(
            T operation,
            object toReturn) where T : IApiOperation
        {
            var handler = new TestApiOperationHandler<T>(toReturn);

            var options = new BlueprintApiOptions(o =>
            {
                o.WithApplicationName("Blueprint.Tests");

                o.UseMiddlewareBuilder<ValidationMiddlewareBuilder>();
                o.UseMiddlewareBuilder<OperationExecutorMiddlewareBuilder>();
                o.UseMiddlewareBuilder<FormatterMiddlewareBuilder>();

                o.AddOperation<T>();
            });

            var container = ConfigureContainer(handler);
            var executor = new ApiOperationExecutorBuilder().Build(options, container);

            var result = await executor.ExecuteWithNewScopeAsync(operation);

            return (result, handler);
        }

        private static ServiceProvider ConfigureContainer<T>(IApiOperationHandler<T> handler) where T : IApiOperation
        {
            var collection = new ServiceCollection();

            collection.AddSingleton(handler);

            return collection.BuildServiceProvider();
        }
    }
}
