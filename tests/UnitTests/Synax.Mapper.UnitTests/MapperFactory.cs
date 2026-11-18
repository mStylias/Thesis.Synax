using Synax.Mapper.Core;

namespace Synax.Mapper.UnitTests;

internal static class MapperFactory
{
    public static IMapper CreateFrom<TProfile>() where TProfile : MapperProfile, new()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<TProfile>());
        return config.CreateMapper();
    }

    public static IMapper CreateFrom(params MapperProfile[] profiles)
    {
        var config = new MapperConfiguration(cfg =>
        {
            foreach (var p in profiles)
                cfg.AddProfile(p);
        });
        return config.CreateMapper();
    }
}
