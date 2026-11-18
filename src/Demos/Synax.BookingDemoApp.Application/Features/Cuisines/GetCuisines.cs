using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Cuisines;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Cuisines;

public class GetCuisinesRequest : IQuery<Result<List<CuisineResponse>, HttpError>>;

public class GetCuisinesHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    IMapper mapper)
    : IQueryHandler<GetCuisinesRequest, Result<List<CuisineResponse>, HttpError>>
{
    private readonly IRepository<Cuisine, BookingContext> _cuisineRepo = unitOfWork.GetRepository<Cuisine>();

    public async Task<Result<List<CuisineResponse>, HttpError>> HandleAsync(
        GetCuisinesRequest request, CancellationToken cancellationToken)
    {
        // Optional-ownership showcase: Cuisine implements IHasOptionalOwner, so the ownership
        // query filter automatically returns the global rows (OwnerId == null) PLUS the
        // caller's own tenant rows — no explicit Where needed here.
        var entities = await _cuisineRepo.Query(q => q.OrderBy(c => c.Name)).ToListAsync(cancellationToken);

        // MapList showcase: in-memory list mapping, including the computed IsGlobal binding.
        return mapper.MapList<CuisineResponse>(entities);
    }
}
