using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class CityService(ICityRepository cityRepository) : ICityService
{
    public async Task<CursorPagedResult<CityResponse>> SearchAsync(CityListQuery query, CancellationToken cancellationToken = default)
    {
        var page = await cityRepository.SearchAsync(query, cancellationToken);
        var items = page.Items.Select(c => c.ToResponse()).ToList();

        return new CursorPagedResult<CityResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<CityResponse> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var city = await cityRepository.GetBySlugAsync(slug, cancellationToken);

        // Locality pages are not public yet: to the outside they don't exist.
        if (city is null || city.Kind != CityKind.Municipality)
            throw new NotFoundException("City not found.");

        return city.ToResponse();
    }
}
