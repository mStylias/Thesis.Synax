namespace Synax.Mapper.UnitTests.TestModels;

// ═══════════════════════════════════════════════════════════════════
//  Source types (simulating EF Core entities)
// ═══════════════════════════════════════════════════════════════════

public class SourceSimple
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}

public class SourceWithNav
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public SourceAddress Address { get; set; } = null!;
    public SourceAddress? OptionalAddress { get; set; }
}

public class SourceAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public class SourceWithCollection
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<SourceItem> Items { get; set; } = [];
}

public class SourceItem
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class SourceComputed
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
}

public class SourceDeep
{
    public int Id { get; set; }
    public SourceWithNav Nested { get; set; } = null!;
}

public class SourceForAutoMap
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Description { get; set; } = "";
    public int Stock { get; set; }
    // Not on dest
    public string InternalCode { get; set; } = "";
}

public class SourceForHashSet
{
    public int Id { get; set; }
    public List<SourceTag> Tags { get; set; } = [];
}

public class SourceTag
{
    public string Name { get; set; } = "";
}

public class SourceForArray
{
    public int Id { get; set; }
    public List<SourceItem> Items { get; set; } = [];
}

// ═══════════════════════════════════════════════════════════════════
//  Destination types (simulating DTOs)
// ═══════════════════════════════════════════════════════════════════

public class DestSimple
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}

public class DestWithNav
{  
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DestAddress Address { get; set; } = null!;
    public DestAddress? OptionalAddress { get; set; }
}

public class DestAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public class DestWithCollection
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<DestItem> Items { get; set; } = [];
}

public class DestItem
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class DestComputed
{
    public string FullName { get; set; } = "";
    public decimal LineTotal { get; set; }
}

public class DestDeep
{
    public int Id { get; set; }
    public DestWithNav Nested { get; set; } = null!;
}

public class DestForAutoMap
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Description { get; set; } = "";
    public int Stock { get; set; }
    // Not on source
    public string CacheKey { get; set; } = "";
}

public class DestWithHashSet
{
    public int Id { get; set; }
    public HashSet<DestTag> Tags { get; set; } = [];
}

public class DestTag
{
    public string Name { get; set; } = "";
}

public class DestWithArray
{
    public int Id { get; set; }
    public DestItem[] Items { get; set; } = [];
}

public class DestWithIEnumerable
{
    public int Id { get; set; }
    public IEnumerable<DestItem> Items { get; set; } = [];
}

public class DestWithIReadOnlyCollection
{
    public int Id { get; set; }
    public IReadOnlyCollection<DestItem> Items { get; set; } = [];
}

// For partial / ignore tests
public class DestPartial
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Ignored { get; set; } = "default_value";
}

// ═══════════════════════════════════════════════════════════════════
//  Record destination types (no parameterless constructor)
// ═══════════════════════════════════════════════════════════════════

// Positional record — primary constructor only, init-only properties.
public record RecordDestSimple(int Id, string Name, decimal Price, DateTime CreatedAt, bool IsActive);

public record RecordDestComputed(string FullName, decimal LineTotal);

public record RecordDestAddress(string Street, string City, string Zip);

public record RecordDestWithNav(
    int Id,
    string Title,
    RecordDestAddress Address,
    RecordDestAddress? OptionalAddress);

public record RecordDestItem(int Id, string Sku, int Quantity, decimal UnitPrice);

public record RecordDestWithCollection(int Id, string Name, List<RecordDestItem> Items);

public record RecordDestWithArray(int Id, RecordDestItem[] Items);

// Positional record with an additional init-only member that is not a constructor
// parameter — exercises the constructor + object-initializer combination.
public record RecordDestMixed(int Id, string Name)
{
    public string Extra { get; init; } = "";
}

// Positional record used for Ignore tests — the ignored constructor parameter
// falls back to its default value.
public record RecordDestPartial(int Id, string Name, string Ignored);

// ═══════════════════════════════════════════════════════════════════
//  Conversion / runtime-type / cycle regression models
// ═══════════════════════════════════════════════════════════════════

// AutoMap nullable lifting (int → int?) and boxing (int → object)
public class SourceNumeric
{
    public int Id { get; set; }
    public int Count { get; set; }
}

public class DestNumericNullable
{
    public int Id { get; set; }
    public int? Count { get; set; }
}

// Standard conversion (int → long), explicit or via AutoMap widening
public class DestNumericWide
{
    public long Id { get; set; }
    public long Count { get; set; }
}

// Narrowing (long → int) must NOT be applied by AutoMap
public class SourceWideValue
{
    public long Value { get; set; }
}

public class DestNarrowValue
{
    public int Value { get; set; }
}

// Enum conversion models
public enum SourceStatus { Unknown = 0, Active = 1, Closed = 2 }

public enum DestStatus { Unknown = 0, Active = 1, Closed = 2 }

public class SourceWithEnum
{
    public int Id { get; set; }
    public SourceStatus Status { get; set; }
    public SourceStatus? OptionalStatus { get; set; }
}

public class DestWithEnumAsString
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
    public string? OptionalStatus { get; set; }
}

public class DestWithEnumAsEnum
{
    public int Id { get; set; }
    public DestStatus Status { get; set; }
    public DestStatus? OptionalStatus { get; set; }
}

// Global converter models (DateTime → string)
public class SourceWithDates
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<DateTime> History { get; set; } = [];
}

public class DestWithDateStrings
{
    public int Id { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? UpdatedAt { get; set; }
    public List<string> History { get; set; } = [];
}

// Simulates an EF Core lazy-loading proxy: a runtime subclass of the mapped type
public class SourceSimpleProxy : SourceSimple;

// Second source type mapping to DestSimple — for heterogeneous MapList dispatch
public class SourceSimpleAlt
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// Self-referencing pair — must be rejected at configuration time, not stack-overflow
public class SourceCyclic
{
    public int Id { get; set; }
    public SourceCyclic? Parent { get; set; }
}

public class DestCyclic
{
    public int Id { get; set; }
    public DestCyclic? Parent { get; set; }
}

// Positional record whose member is an aggregate over a navigation. The shape that
// exposed the missing constructor member names: a provider cannot translate a subquery
// left inside an ORDER BY, so an unsimplified constructor is fatal here rather than
// merely verbose.
public record RecordDestWithCount(int Id, int OrderCount);

// Mutually-referencing pairs (A → B → A) — same requirement as above
public class SourceCustomer
{
    public int Id { get; set; }
    public List<SourceOrder> Orders { get; set; } = [];
}

public class SourceOrder
{
    public int Id { get; set; }
    public SourceCustomer? Customer { get; set; }
}

public class DestCustomer
{
    public int Id { get; set; }
    public List<DestOrder> Orders { get; set; } = [];
}

public class DestOrder
{
    public int Id { get; set; }
    public DestCustomer? Customer { get; set; }
}

// For AutoMap with unregistered nested mapping (should skip)
public class SourceWithUnmappedNav
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public SourceAddress UnmappedAddress { get; set; } = null!;
}

public class DestWithUnmappedNav
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    // DestAddress has no registered mapping from SourceAddress in this profile
    public DestAddress UnmappedAddress { get; set; } = null!;
}

