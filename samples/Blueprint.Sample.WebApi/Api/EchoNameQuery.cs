﻿using System.Threading.Tasks;
using Blueprint.Api;

namespace Blueprint.Sample.WebApi.Api
{
    [RootLink("echoName")]
    public class EchoNameQuery : IQuery
    {
        public string Name { get; set; }
    }

    public class EchoNameQueryHandler : IApiOperationHandler<EchoNameQuery>
    {
        public async Task<object> Invoke(EchoNameQuery operation, ApiOperationContext apiOperationContext)
        {
            return new { operation.Name };
        }
    }
}