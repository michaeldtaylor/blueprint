using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Blueprint.Authorisation;
using Blueprint.Http;
using Blueprint.Http.MessagePopulation;
using Blueprint.Middleware;
using Blueprint.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Namotion.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using NJsonSchema;
using NJsonSchema.Generation;
using NSwag;

namespace Blueprint.OpenApi
{
    /// <summary>
    /// An <see cref="IQuery" /> that can will return an OpenAPI representation of the
    /// <see cref="ApiDataModel" /> of the current API.
    /// </summary>
    [AllowAnonymous]
    [RootLink("/openapi")]
    [UnexposedOperation]
    public class OpenApiQuery : IQuery<PlainTextResult>
    {
        /// <summary>
        /// Returns the OpenAPI representation of the given <see cref="ApiDataModel" />.
        /// </summary>
        /// <param name="httpContext">The <see cref="HttpContext" />.</param>
        /// <param name="serviceProvider">Service provider used to create new <see cref="ISchemaProcessor" /> instances.</param>
        /// <param name="apiDataModel">The current data model.</param>
        /// <param name="messagePopulationSources">The registered message population sources.</param>
        /// <param name="options">The options to configure the OpenAPI document</param>
        /// <returns>An OpenAPI representation.</returns>
        public PlainTextResult Invoke(
            HttpContext httpContext,
            IServiceProvider serviceProvider,
            ApiDataModel apiDataModel,
            IEnumerable<IMessagePopulationSource> messagePopulationSources,
            IOptions<OpenApiOptions> options)
        {
            var openApiOptions = options.Value;
            var basePath = httpContext.GetBlueprintBasePath();

            var document = new OpenApiDocument
            {
                // Special case a base path of just "/" to avoid "//". We prepend "/" to
                // indicate this is relative to the URL the document was accessed at
                BasePath = basePath == "/" ? "/" : "/" + basePath,
            };

            var jsonSchemaGeneratorSettings = new JsonSchemaGeneratorSettings
            {
                SerializerSettings = new JsonSerializerSettings
                {
                    ContractResolver = new BlueprintContractResolver(apiDataModel, messagePopulationSources),

                    Converters =
                    {
                        new StringEnumConverter(),
                    },
                },

                SchemaType = SchemaType.JsonSchema,

                FlattenInheritanceHierarchy = true,

                ReflectionService = new BlueprintReflectionService(),

                SchemaNameGenerator = new BlueprintSchemaNameGenerator(),

                DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull,
            };

            foreach (var processor in openApiOptions.SchemaProcessors)
            {
                jsonSchemaGeneratorSettings.SchemaProcessors.Add(
                    (ISchemaProcessor)ActivatorUtilities.CreateInstance(serviceProvider, processor, apiDataModel));
            }

            openApiOptions.ConfigureSettings?.Invoke(jsonSchemaGeneratorSettings);

            var generator = new JsonSchemaGenerator(jsonSchemaGeneratorSettings);

            var openApiDocumentSchemaResolver = new OpenApiDocumentSchemaResolver(document, jsonSchemaGeneratorSettings);

            foreach (var operation in apiDataModel.Operations)
            {
                if (!operation.IsExposed)
                {
                    continue;
                }

                var httpData = operation.GetFeatureData<HttpOperationFeatureData>();

                foreach (var route in operation.Links)
                {
                    var pathUrl = "/" + route.RoutingUrl;

                    if (!document.Paths.TryGetValue(pathUrl, out var openApiPathItem))
                    {
                        openApiPathItem = new OpenApiPathItem();
                        document.Paths[pathUrl] = openApiPathItem;
                    }

                    var openApiOperation = new OpenApiOperation
                    {
                        OperationId = operation.Name,
                        Summary = operation.OperationType.GetXmlDocsSummary(),
                        Description = operation.OperationType.GetXmlDocsRemarks(),
                    };

                    // Use the last namespace segment as a tag of this operation, which provides a generally
                    // better structure when generating documentation or SDK clients
                    if (operation.OperationType.Namespace != null)
                    {
                        openApiOperation.Tags.Add(operation.OperationType.Namespace.Split('.').Last());
                    }

                    var httpMethod = operation.GetFeatureData<HttpOperationFeatureData>().HttpMethod;

                    var allOwned = messagePopulationSources
                        .SelectMany(s => s.GetOwnedProperties(apiDataModel, operation))
                        .ToList();

                    // First, add the explicit "owned" properties, those that we know come from a particular
                    // place and are therefore _not_ part of the body
                    foreach (var property in operation.Properties)
                    {
                        // We are only considering "owned" parameters here. All non-owned properties will
                        // be part of the "body" of this command, as handled below UNLESS this is a GET request,
                        // in which case we determine the parameter comes from the query (if not explictly
                        // overriden)
                        if (!allOwned.Contains(property) && httpMethod != "GET")
                        {
                            continue;
                        }

                        var isRoute = route.Placeholders.Any(p => p.Property == property);

                        openApiOperation.Parameters.Add(new OpenApiParameter
                        {
                            Kind = isRoute ? OpenApiParameterKind.Path : ToKind(property),

                            Name = HttpPartMessagePopulationSource.GetPartKey(property),

                            IsRequired = isRoute ||
                                         property.ToContextualProperty().Nullability == Nullability.NotNullable ||
                                         property.GetCustomAttributes<RequiredAttribute>().Any(),

                            Schema = generator.Generate(property.PropertyType),

                            Description = property.GetXmlDocsSummary(),
                        });
                    }

                    // GETs will NOT have their body parsed, meaning we need not handle body parameter at all
                    if (httpMethod != "GET")
                    {
                        // The body schema would contain all non-owned properties (owned properties are
                        // handled above as coming from a specific part of the HTTP request).
                        var bodySchema = GetOrAddJsonSchema(operation.OperationType, document, generator, openApiDocumentSchemaResolver);

                        if (bodySchema != null)
                        {
                            openApiOperation.RequestBody = new OpenApiRequestBody
                            {
                                Content =
                                {
                                    ["application/json"] = new OpenApiMediaType
                                    {
                                        Schema = bodySchema,
                                    },
                                },
                            };
                        }
                    }

                    foreach (var response in operation.Responses)
                    {
                        var httpStatusCode = ToHttpStatusCode(response).ToString();

                        if (!openApiOperation.Responses.TryGetValue(httpStatusCode, out var oaResponse))
                        {
                            oaResponse = new OpenApiResponse
                            {
                                Description = response.Description,
                            };

                            openApiOperation.Responses[httpStatusCode] = oaResponse;
                        }

                        // Note below assignments are once-only. We always return a ProblemResult from HTTP,
                        // so we can assume we only need to set the failure schema's once, and can
                        // only return a single type for success.
                        //
                        // If we override Content then Examples are removed.
                        if (response.Type == typeof(PlainTextResult))
                        {
                            if (!oaResponse.Content.ContainsKey("text/plain"))
                            {
                                oaResponse.Content["text/plain"] = new OpenApiMediaType
                                {
                                    Schema = new JsonSchema
                                    {
                                        Type = JsonObjectType.String,
                                    },
                                };
                            }
                        }
                        else
                        {
                            if (!oaResponse.Content.ContainsKey("application/json"))
                            {
                                // Assume for now we always return JSON
                                oaResponse.Content["application/json"] = new OpenApiMediaType
                                {
                                    Schema = GetOrAddJsonSchema(
                                        GetResponseType(response),
                                        document,
                                        generator,
                                        openApiDocumentSchemaResolver),
                                };
                            }
                        }

                        // When we have an ApiException that has additional metadata attached we try to find
                        // "type", which can be declared on the <exception /> tag to enable us to provide
                        // more details about the types of failures within a status code that could be expected
                        if (response.Metadata != null && response.Type == typeof(ApiException))
                        {
                            if (response.Metadata.TryGetValue("type", out var problemType))
                            {
                                var examples = oaResponse.Examples as Dictionary<string, object> ?? new Dictionary<string, object>();
                                oaResponse.Examples = examples;

                                examples[problemType.ToPascalCase()] = new
                                {
                                    summary = response.Description,

                                    value = new
                                    {
                                        type = problemType,
                                    },
                                };
                            }
                        }
                    }

                    openApiPathItem[ToOpenApiOperationMethod(httpData.HttpMethod)] = openApiOperation;
                }
            }

            openApiOptions.PostConfigure?.Invoke(document);

            return new PlainTextResult(document.ToJson(openApiOptions.SchemaType, openApiOptions.Formatting))
            {
                ContentType = "application/json",
            };
        }

        internal static Type GetActualType(Type type)
        {
            // We do not output the individual event types, but instead consolidate down to only a concrete
            // implementation of the well-known subclasses (i.e. we would only have _one_ ResourceUpdated per
            // type instead of multiple for every subclass.
            if (type.IsOfGenericType(typeof(ResourceUpdated<>), out var concreteUpdatedGenericType) &&
                !type.IsGenericType)
            {
                return GetActualType(concreteUpdatedGenericType);
            }

            if (type.IsAbstract == false && type.IsOfGenericType(typeof(ResourceCreated<>), out var concreteCreatedType) &&
                !type.IsGenericType)
            {
                return GetActualType(concreteCreatedType);
            }

            if (type.IsAbstract == false && type.IsOfGenericType(typeof(ResourceDeleted<>), out var concreteDeletedType) &&
                !type.IsGenericType)
            {
                return GetActualType(concreteDeletedType);
            }

            return type;
        }

        private static Type GetResponseType(ResponseDescriptor response)
        {
            return response.HttpStatus switch
            {
                var x when x >= 200 && x <= 299 => response.Type,
                422 => typeof(ValidationProblemDetails),
                _ => typeof(ProblemDetails)
            };
        }

        private static int ToHttpStatusCode(ResponseDescriptor responseCategory)
        {
            return responseCategory.HttpStatus;
        }

        private static OpenApiParameterKind ToKind(PropertyInfo property)
        {
            if (property.HasAttribute(typeof(FromHeaderAttribute), false))
            {
                return OpenApiParameterKind.Header;
            }

            if (property.HasAttribute(typeof(FromCookieAttribute), false))
            {
                return OpenApiParameterKind.Cookie;
            }

            if (property.HasAttribute(typeof(FromQueryAttribute), false))
            {
                return OpenApiParameterKind.Query;
            }

            return OpenApiParameterKind.Query;
        }

        private static JsonSchema GetOrAddJsonSchema(
            Type type,
            OpenApiDocument document,
            JsonSchemaGenerator generator,
            JsonSchemaResolver jsonSchemaResolver)
        {
            type = GetActualType(type);

            var jsonSchemaName = generator.Settings.SchemaNameGenerator.Generate(type);

            // We try to find in the "#/components/schemas" namespace an existing schema. If it does
            // not exist we will add one, set it's document path correctly
            if (!document.Components.Schemas.TryGetValue(jsonSchemaName, out var jsonSchema))
            {
                jsonSchema = generator.Generate(type, jsonSchemaResolver);

                if (jsonSchema.Properties.Any() == false)
                {
                    return null;
                }

                jsonSchema.DocumentPath = "#/components/schemas/" + jsonSchemaName;
                jsonSchema.Id = jsonSchemaName;

                document.Components.Schemas[jsonSchemaName] = jsonSchema;
            }

            // The actual JsonSchema returned is only ever a reference to the common one
            // stored in "#/components/schemas"
            return new JsonSchema
            {
                Type = JsonObjectType.Object,
                Reference = jsonSchema,
            };
        }

        private static string ToOpenApiOperationMethod(string method)
        {
            if (method == "GET")
            {
                return OpenApiOperationMethod.Get;
            }

            if (method == "DELETE")
            {
                return OpenApiOperationMethod.Delete;
            }

            if (method == "HEAD")
            {
                return OpenApiOperationMethod.Head;
            }

            if (method == "OPTIONS")
            {
                return OpenApiOperationMethod.Options;
            }

            if (method == "POST")
            {
                return OpenApiOperationMethod.Post;
            }

            if (method == "PUT")
            {
                return OpenApiOperationMethod.Put;
            }

            throw new ArgumentOutOfRangeException(nameof(method));
        }

        private class BlueprintSchemaNameGenerator : DefaultSchemaNameGenerator
        {
            public override string Generate(Type type)
            {
                return base.Generate(type)
                    .Replace("ApiResource", string.Empty)
                    .Replace("PagedOf", "Paged");
            }
        }

        private class BlueprintContractResolver : CamelCasePropertyNamesContractResolver
        {
            private readonly ApiDataModel apiDataModel;
            private readonly IEnumerable<IMessagePopulationSource> messagePopulationSources;

            public BlueprintContractResolver(ApiDataModel apiDataModel, IEnumerable<IMessagePopulationSource> messagePopulationSources)
            {
                this.apiDataModel = apiDataModel;
                this.messagePopulationSources = messagePopulationSources;
            }

            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                var descriptor = apiDataModel.Operations.SingleOrDefault(o => o.OperationType == objectType);

                if (descriptor == null)
                {
                    return base.GetSerializableMembers(objectType);
                }

                var allOwned = messagePopulationSources
                    .SelectMany(s => s.GetOwnedProperties(apiDataModel, descriptor))
                    .ToList();

                return base.GetSerializableMembers(objectType)
                    .Where(p => !allOwned.Contains(p))
                    .ToList();
            }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var baseProperty = base.CreateProperty(member, memberSerialization);

                if (member.ToContextualMember().Nullability == Nullability.NotNullable)
                {
                    baseProperty.Required = Required.Always;
                }

                if (member.DeclaringType == typeof(ResourceEvent) && member.Name == nameof(ResourceEvent.Data))
                {
                    baseProperty.Ignored = true;
                }

                if (member.DeclaringType == typeof(ApiResource))
                {
                    if (member.Name == nameof(ApiResource.Object) ||
                        member.Name == nameof(ApiResource.Links))
                    {
                        baseProperty.Required = Required.Always;
                    }
                }

                if (member.DeclaringType == typeof(Link))
                {
                    if (member.Name == nameof(Link.Href) ||
                        member.Name == nameof(Link.Type))
                    {
                        baseProperty.Required = Required.Always;
                    }
                }

                if (member.DeclaringType.IsOfGenericType(typeof(PagedApiResource<>)))
                {
                    if (member.Name == nameof(PagedApiResource<object>.Values))
                    {
                        baseProperty.Required = Required.Always;
                    }
                }

                if (member.DeclaringType == typeof(ResourceEvent))
                {
                    if (member.Name == nameof(ResourceEvent.Object) ||
                        member.Name == nameof(ResourceEvent.EventId) ||
                        member.Name == nameof(ResourceEvent.ResourceObject) ||
                        member.Name == nameof(ResourceEvent.Data))
                    {
                        baseProperty.Required = Required.Always;
                    }
                }

                if (member.DeclaringType.IsOfGenericType(typeof(ResourceEvent<>)))
                {
                    if (member.Name == nameof(ResourceEvent.Data))
                    {
                        baseProperty.Required = Required.Always;
                    }
                }

                return baseProperty;
            }
        }

        private class BlueprintReflectionService : DefaultReflectionService
        {
            public override bool IsNullable(ContextualType contextualType, ReferenceTypeNullHandling defaultReferenceTypeNullHandling)
            {
                return false;
            }
        }

        private class OpenApiDocumentSchemaResolver : JsonSchemaResolver
        {
            private readonly ITypeNameGenerator typeNameGenerator;

            /// <summary>Initializes a new instance of the <see cref="OpenApiDocumentSchemaResolver" /> class.</summary>
            /// <param name="document">The Open API document.</param>
            /// <param name="settings">The settings.</param>
            public OpenApiDocumentSchemaResolver(OpenApiDocument document, JsonSchemaGeneratorSettings settings)
                : base(document, settings)
            {
                if (document == null)
                {
                    throw new ArgumentNullException(nameof(document));
                }

                typeNameGenerator = settings.TypeNameGenerator;
            }

            private OpenApiDocument Document => (OpenApiDocument)RootObject;

            /// <inheritdoc/>
            public override void AppendSchema(JsonSchema schema, string typeNameHint)
            {
                var schemas = Document.Components.Schemas;

                if (schemas.Values.Contains(schema))
                {
                    return;
                }

                schemas[typeNameGenerator.Generate(schema, typeNameHint, schemas.Keys)] = schema;
            }
        }
    }
}
