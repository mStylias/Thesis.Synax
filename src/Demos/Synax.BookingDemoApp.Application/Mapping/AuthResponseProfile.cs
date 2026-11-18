using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Mapping;

/// <summary>
/// The user wire shape returned by login and <c>/auth/me</c>. Only ever mapped in memory —
/// the global DateTime → string converter below is not SQL-translatable, so don't use this
/// map in QueryDto/SQL projections.
/// </summary>
public class UserResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string MemberSince { get; set; } = string.Empty;
}

public class AuthResponseProfile : MapperProfile
{
    public AuthResponseProfile()
    {
        // Global-converter showcase: applies wherever a DateTime source member binds to a
        // string destination member — explicit bindings and automapped ones alike.
        ConvertUsing<DateTime, string>(d => d.ToString("yyyy-MM-dd"));

        // Automapping binds the same-named members (Role enum → string is a built-in safe
        // conversion); MemberSince gets an explicit binding to CreatedAt, and the global
        // converter above turns that DateTime into the formatted string.
        CreateMap<AppUser, UserResponse>()
            .For(d => d.MemberSince, s => s.CreatedAt);
    }
}
