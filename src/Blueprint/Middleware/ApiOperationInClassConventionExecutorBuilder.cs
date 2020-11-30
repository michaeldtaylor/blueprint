using System.Reflection;
using System.Threading.Tasks;
using Blueprint.CodeGen;
using Blueprint.Compiler;
using Blueprint.Compiler.Frames;
using Blueprint.Compiler.Model;

namespace Blueprint.Middleware
{
    /// <summary>
    /// An <see cref="IOperationExecutorBuilder" /> that will use a public method that exists on the operation class
    /// itself.
    /// </summary>
    public class ApiOperationInClassConventionExecutorBuilder : IOperationExecutorBuilder
    {
        private readonly MethodInfo _method;

        /// <summary>
        /// Creates a new instance of the <see cref="ApiOperationInClassConventionExecutorBuilder" /> that represents the given <see cref="ApiOperationDescriptor"/>.
        /// </summary>
        /// <param name="operation">The operation this builder handles.</param>
        /// <param name="method">The method that is to be executed.</param>
        public ApiOperationInClassConventionExecutorBuilder(ApiOperationDescriptor operation, MethodInfo method)
        {
            this.Operation = operation;
            this._method = method;
        }

        /// <inheritdoc />
        public ApiOperationDescriptor Operation { get; }

        /// <inheritdoc />
        public Variable Build(MiddlewareBuilderContext context)
        {
            // We rely on the compiler infrastructure to make the correct calls, to the correct type (i.e. the actual
            // operation), and to fill in the parameters of that method as required.
            var handlerInvokeCall = new MethodCall(context.Descriptor.OperationType, this._method);

            // Note that although we know the handler type at compile time, we still specify it as a
            // parameter to logging so that it is output as a structured value (as it changes between
            // invocations)
            context.AppendFrames(
                LogFrame.Information(
                    "Executing API operation with handler {HandlerType}",
                    $"\"{context.Descriptor.OperationType.Name}\""),
                handlerInvokeCall);

            // We have a void, or a Task (i.e. async with no return) so we will convert to a 'NoResult'
            if (handlerInvokeCall.ReturnVariable == null || handlerInvokeCall.ReturnVariable.VariableType == typeof(Task))
            {
                var emptyResultCreation = new VariableCreationFrame(
                    typeof(NoResultOperationResult),
                    $"{typeof(NoResultOperationResult).FullNameInCode()}.{nameof(NoResultOperationResult.Instance)};");

                context.AppendFrames(emptyResultCreation);

                return emptyResultCreation.CreatedVariable;
            }

            return handlerInvokeCall.ReturnVariable;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{this.Operation.OperationType.Name}.{this._method.Name}";
        }
    }
}
