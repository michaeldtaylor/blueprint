﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Blueprint.Compiler
{
    public class GeneratedAssembly
    {
        private readonly GenerationRules _generationRules;
        private readonly HashSet<Assembly> _assemblies = new HashSet<Assembly>();

        public GeneratedAssembly(GenerationRules generationRules)
        {
            this._generationRules = generationRules;
        }

        public List<GeneratedType> GeneratedTypes { get; } = new List<GeneratedType>();

        public void ReferenceAssembly(Assembly assembly)
        {
            this._assemblies.Add(assembly);
        }

        /// <summary>
        /// Creates a new <see cref="GeneratedType" /> and adds it to this assembly.
        /// </summary>
        /// <param name="namespace">The namespace of the type.</param>
        /// <param name="typeName">The name of the type / class.</param>
        /// <returns>A new <see cref="GeneratedType" />.</returns>
        /// <exception cref="ArgumentException">If a type already exists.</exception>
        public GeneratedType AddType(string @namespace, string typeName, Type baseType)
        {
            if (this.GeneratedTypes.Any(t => t.Namespace == @namespace && t.TypeName == typeName))
            {
                throw new ArgumentException($"A type already exists at {@namespace}.{typeName}");
            }

            var generatedType = new GeneratedType(this, typeName, @namespace ?? this._generationRules.AssemblyName);

            if (baseType.IsInterface)
            {
                generatedType.Implements(baseType);
            }
            else
            {
                generatedType.InheritsFrom(baseType);
            }

            this.GeneratedTypes.Add(generatedType);

            return generatedType;
        }

        public void CompileAll(IAssemblyGenerator generator)
        {
            foreach (var assemblyReference in this._assemblies)
            {
                generator.ReferenceAssembly(assemblyReference);
            }

            foreach (var generatedType in this.GeneratedTypes)
            {
                foreach (var x in generatedType.AssemblyReferences())
                {
                    generator.ReferenceAssembly(x);
                }

                // We generate the code for the type upfront as we allow adding namespaces etc. during the rendering of
                // frames so we need to do those, and _then_ gather namespaces
                // A rough estimate of 3000 characters per method with 2 being used, plus 1000 for ctor.
                var typeWriter = new SourceWriter((3000 * 2) + 1000);
                generatedType.Write(typeWriter);

                var namespaces = generatedType
                    .AllInjectedFields
                    .Select(x => x.VariableType.Namespace)
                    .Concat(new[] { typeof(Task).Namespace })
                    .Concat(generatedType.Namespaces)
                    .Distinct()
                    .ToList();

                var writer = new SourceWriter();

                writer.Comment("<auto-generated />");
                writer.Comment(generatedType.TypeName);
                writer.BlankLine();

                foreach (var ns in namespaces.OrderBy(x => x))
                {
                    writer.UsingNamespace(ns);
                }

                writer.BlankLine();

                writer.Namespace(generatedType.Namespace);

                writer.WriteLines(typeWriter.Code());

                writer.FinishBlock();

                var code = writer.Code();

                generatedType.SourceCode = code;
                generator.AddFile($"{generatedType.Namespace.Replace(".", "/")}/{generatedType.TypeName}.cs", code);
            }

            var assembly = generator.Generate(this._generationRules);

            var generated = assembly.GetExportedTypes().ToArray();

            foreach (var generatedType in this.GeneratedTypes)
            {
                generatedType.FindType(generated);
            }
        }
    }
}
