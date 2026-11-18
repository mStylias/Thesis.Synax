namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

public class RankedBookingViewDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsPreferred { get; set; }
}
