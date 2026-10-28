using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SupportDesk.Presentation.Authentication;

/// <summary>Document anonymous operations accurately under the global bearer requirement.</summary>
public sealed class AnonymousOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
            || context.MethodInfo.DeclaringType?.IsDefined(typeof(AllowAnonymousAttribute), inherit: true) == true)
        {
            operation.Security = [];
        }
    }
}
