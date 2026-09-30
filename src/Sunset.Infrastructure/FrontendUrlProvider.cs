using Microsoft.Extensions.Options;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure;

public class FrontendUrlProvider(IOptions<FrontendOptions> options) : IFrontendUrlProvider
{
    public string BaseUrl => options.Value.BaseUrl;
}
