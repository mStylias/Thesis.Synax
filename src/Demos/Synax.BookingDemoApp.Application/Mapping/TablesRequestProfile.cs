using Synax.BookingDemoApp.Application.Features.Tables;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Mapping;

/// <summary>
/// Request → entity map. Generated/derived members use <c>For</c>; everything the interceptors
/// stamp (ownership, soft delete) is explicitly <c>Ignore</c>d so
/// <c>AssertConfigurationIsValid()</c> knows they are intentional. Location maps automatically —
/// same enum type on both sides.
/// </summary>
public class TablesRequestProfile : MapperProfile
{
    public TablesRequestProfile()
    {
        CreateMap<CreateTableRequest, Table>()
            .For(d => d.Id, _ => Guid.CreateVersion7())
            // Stamped by the ownership/soft-delete interceptors on save:
            .Ignore(d => d.OwnerId)
            .Ignore(d => d.CreatedAt)
            .Ignore(d => d.IsDeleted)
            .Ignore(d => d.DeletedAt);
    }
}
