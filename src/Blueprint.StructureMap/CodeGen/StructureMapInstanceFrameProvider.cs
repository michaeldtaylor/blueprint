﻿using System;
using System.Linq;
using Blueprint.Api;
using Blueprint.Api.CodeGen;
using Blueprint.Compiler;
using Blueprint.Compiler.Model;
using StructureMap;
using StructureMap.Pipeline;

namespace Blueprint.StructureMap.CodeGen
{
    public class StructureMapInstanceFrameProvider : IInstanceFrameProvider
    {
        private readonly IContainer container;

        public StructureMapInstanceFrameProvider(IContainer container)
        {
            this.container = container;
        }

        public GetInstanceFrame<T> VariableFromContainer<T>(GeneratedType generatedType, Type toLoad)
        {
            var config = container.Model.For(toLoad);

            if (config.HasImplementations() && config.Instances.Count() == 1)
            {
                // When there is only one possible type that could be created from the IoC container
                // we can do a little more optimisation.
                var instanceRef = config.Instances.Single();

                if (instanceRef.Lifecycle is SingletonLifecycle)
                {
                    // We have a singleton object, which means we can have this injected at build time of the
                    // pipeline executor which will only be constructed once.
                    var injected = new InjectedField(toLoad);

                    generatedType.AllInjectedFields.Add(injected);

                    return new InjectedFrame<T>(injected);
                }

                // Small tweak to resolve the actual known type. Makes generated code a little nicer as it
                // makes it obvious what is _actually_ going to be built without knowledge of the container
                // setup
                return new TransientInstanceFrame<T>(toLoad, instanceRef.ReturnedType);
            }

            return new TransientInstanceFrame<T>(toLoad);
        }
    }
}