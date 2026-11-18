using Synax.Mapper.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests.MappingProfiles;

public class BookingMappingProfile : MapperProfile
{
    public BookingMappingProfile()
    {
        CreateMap<Booking, ReferenceViewDto>();
    }
}
