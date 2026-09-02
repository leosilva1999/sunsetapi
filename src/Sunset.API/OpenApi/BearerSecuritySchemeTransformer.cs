using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Sunset.API.OpenApi;

public class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var scheme = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter the JWT access token returned by /auth/login or /auth/register.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes["Bearer"] = scheme;

        document.SecurityRequirements.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = "Bearer", Type = ReferenceType.SecurityScheme } }] = [],
        });

        return Task.CompletedTask;
    }
}
