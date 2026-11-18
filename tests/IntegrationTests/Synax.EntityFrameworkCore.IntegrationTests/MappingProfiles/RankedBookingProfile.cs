using Synax.Mapper.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests.MappingProfiles;

public class RankedBookingProfile : MapperProfile
{
    public RankedBookingProfile()
    {
        var preferredName = Parameter<string>("preferredName");

        CreateMap<Booking, RankedBookingViewDto>()
            .For(d => d.IsPreferred, s => s.Name == preferredName.Value);
    }
}
