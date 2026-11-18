using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.BookingDemoApp.Domain;

/// <summary>
/// The booking module's persistence context tag. Handlers resolve
/// <c>IUnitOfWork&lt;BookingContext&gt;</c> / <c>IRepository&lt;Booking, BookingContext&gt;</c> against
/// this type, which lives in Domain — so the Application layer never has to reference
/// Infrastructure (and with it Npgsql and <c>BookingDbContext</c>) just to name its own database.
/// <para>
/// Infrastructure binds it to the real context in one place:
/// <c>options.ForDbContext&lt;BookingDbContext&gt;().As&lt;BookingContext&gt;()</c>.
/// </para>
/// </summary>
public sealed class BookingContext : ISynaxContext;
