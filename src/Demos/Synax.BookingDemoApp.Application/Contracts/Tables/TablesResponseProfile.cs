using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Contracts.Tables;

public class TablesResponseProfile : MapperProfile
{
    public TablesResponseProfile()
    {
        // Automap covers everything, including enum→string for Location. This element map is
        // what makes the Restaurant detail's nested Tables collection bind.
        CreateMap<Table, TableResponse>();
    }
}
