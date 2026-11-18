using Synax.Mapper.Core;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests.Profiles;

public class SimpleProfile : MapperProfile
{
    public SimpleProfile()
    {
        CreateMap<SourceSimple, DestSimple>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Name, s => s.Name)
            .For(d => d.Price, s => s.Price)
            .For(d => d.CreatedAt, s => s.CreatedAt)
            .For(d => d.IsActive, s => s.IsActive);
    }
}

public class AutoMapProfile : MapperProfile
{
    public AutoMapProfile()
    {
        CreateMap<SourceSimple, DestSimple>();
    }
}

public class ComputedProfile : MapperProfile
{
    public ComputedProfile()
    {
        CreateMap<SourceComputed, DestComputed>()
            .For(d => d.FullName, s => s.FirstName + " " + s.LastName)
            .For(d => d.LineTotal, s => (s.UnitPrice * s.Quantity) - s.Discount);
    }
}

public class AddressProfile : MapperProfile
{
    public AddressProfile()
    {
        CreateMap<SourceAddress, DestAddress>();
    }
}

public class NestedProfile : MapperProfile
{
    public NestedProfile()
    {
        CreateMap<SourceAddress, DestAddress>();

        CreateMap<SourceWithNav, DestWithNav>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Title, s => s.Title)
            .For(d => d.Address, s => s.Address)
            .For(d => d.OptionalAddress, s => s.OptionalAddress);
    }
}

public class ItemProfile : MapperProfile
{
    public ItemProfile()
    {
        CreateMap<SourceItem, DestItem>();
    }
}

public class CollectionListProfile : MapperProfile
{
    public CollectionListProfile()
    {
        CreateMap<SourceItem, DestItem>();

        CreateMap<SourceWithCollection, DestWithCollection>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Name, s => s.Name)
            .For(d => d.Items, s => s.Items);
    }
}

public class CollectionArrayProfile : MapperProfile
{
    public CollectionArrayProfile()
    {
        CreateMap<SourceItem, DestItem>();

        CreateMap<SourceForArray, DestWithArray>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Items, s => s.Items);
    }
}

public class CollectionHashSetProfile : MapperProfile
{
    public CollectionHashSetProfile()
    {
        CreateMap<SourceTag, DestTag>();

        CreateMap<SourceForHashSet, DestWithHashSet>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Tags, s => s.Tags);
    }
}

public class CollectionIEnumerableProfile : MapperProfile
{
    public CollectionIEnumerableProfile()
    {
        CreateMap<SourceItem, DestItem>();

        CreateMap<SourceWithCollection, DestWithIEnumerable>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Items, s => s.Items);
    }
}

public class CollectionIReadOnlyProfile : MapperProfile
{
    public CollectionIReadOnlyProfile()
    {
        CreateMap<SourceItem, DestItem>();

        CreateMap<SourceWithCollection, DestWithIReadOnlyCollection>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Items, s => s.Items);
    }
}

public class IgnoreProfile : MapperProfile
{
    public IgnoreProfile()
    {
        CreateMap<SourceSimple, DestPartial>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Name, s => s.Name)
            .Ignore(d => d.Ignored);
    }
}

public class AutoMapWithIgnoreProfile : MapperProfile
{
    public AutoMapWithIgnoreProfile()
    {
        CreateMap<SourceForAutoMap, DestForAutoMap>()
            .Ignore(d => d.CacheKey);
    }
}

public class DeepNestedProfile : MapperProfile
{
    public DeepNestedProfile()
    {
        CreateMap<SourceAddress, DestAddress>();

        CreateMap<SourceWithNav, DestWithNav>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Title, s => s.Title)
            .For(d => d.Address, s => s.Address)
            .For(d => d.OptionalAddress, s => s.OptionalAddress);

        CreateMap<SourceDeep, DestDeep>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Nested, s => s.Nested);
    }
}

/// <summary>
/// Profile that intentionally defines mappings in reverse order
/// to test forward-reference resolution.
/// </summary>
public class ForwardReferenceProfile : MapperProfile
{
    public ForwardReferenceProfile()
    {
        // Parent mapping references child mapping that is defined AFTER it
        CreateMap<SourceWithNav, DestWithNav>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Title, s => s.Title)
            .For(d => d.Address, s => s.Address)
            .For(d => d.OptionalAddress, s => s.OptionalAddress);

        // Child mapping defined second — must be lazily resolved
        CreateMap<SourceAddress, DestAddress>();
    }
}

// ═══════════════════════════════════════════════════════════════════
// AutoMap with nested / collection resolution
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Uses only AutoMap() to map a parent with a nested entity —
/// the nested mapping is resolved automatically.
/// </summary>
public class AutoMapNestedProfile : MapperProfile
{
    public AutoMapNestedProfile()
    {
        CreateMap<SourceAddress, DestAddress>();

        CreateMap<SourceWithNav, DestWithNav>();
    }
}

/// <summary>
/// Uses only AutoMap() to map a parent with a collection property —
/// the element mapping is resolved automatically.
/// </summary>
public class AutoMapCollectionProfile : MapperProfile
{
    public AutoMapCollectionProfile()
    {
        CreateMap<SourceItem, DestItem>();

        CreateMap<SourceWithCollection, DestWithCollection>();
    }
}

/// <summary>
/// Uses only AutoMap() across three levels of nesting.
/// </summary>
public class AutoMapDeepNestedProfile : MapperProfile
{
    public AutoMapDeepNestedProfile()
    {
        CreateMap<SourceAddress, DestAddress>();

        CreateMap<SourceWithNav, DestWithNav>();

        CreateMap<SourceDeep, DestDeep>();
    }
}

// ═══════════════════════════════════════════════════════════════════
// Record destinations (constructor-based projection)
// ═══════════════════════════════════════════════════════════════════

/// <summary>AutoMap into a positional record — every property is a constructor parameter.</summary>
public class RecordAutoMapProfile : MapperProfile
{
    public RecordAutoMapProfile()
    {
        CreateMap<SourceSimple, RecordDestSimple>();
    }
}

/// <summary>A positional record whose member is an aggregate over a navigation.</summary>
public class RecordWithCountProfile : MapperProfile
{
    public RecordWithCountProfile()
    {
        CreateMap<SourceCustomer, RecordDestWithCount>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.OrderCount, s => s.Orders.Count);
    }
}

/// <summary>Explicit <c>For</c> bindings into a positional record.</summary>
public class RecordExplicitProfile : MapperProfile
{
    public RecordExplicitProfile()
    {
        CreateMap<SourceSimple, RecordDestSimple>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Name, s => s.Name)
            .For(d => d.Price, s => s.Price)
            .For(d => d.CreatedAt, s => s.CreatedAt)
            .For(d => d.IsActive, s => s.IsActive)
            .AutoMap(false);
    }
}

/// <summary>Computed <c>For</c> expressions feeding constructor parameters.</summary>
public class RecordComputedProfile : MapperProfile
{
    public RecordComputedProfile()
    {
        CreateMap<SourceComputed, RecordDestComputed>()
            .For(d => d.FullName, s => s.FirstName + " " + s.LastName)
            .For(d => d.LineTotal, s => (s.UnitPrice * s.Quantity) - s.Discount);
    }
}

/// <summary>Explicit nested / optional-nested For bindings into a record (records nested in records).</summary>
public class RecordNestedProfile : MapperProfile
{
    public RecordNestedProfile()
    {
        CreateMap<SourceAddress, RecordDestAddress>();

        CreateMap<SourceWithNav, RecordDestWithNav>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Title, s => s.Title)
            .For(d => d.Address, s => s.Address)
            .For(d => d.OptionalAddress, s => s.OptionalAddress);
    }
}

/// <summary>AutoMap resolving nested + optional record members automatically.</summary>
public class RecordAutoNestedProfile : MapperProfile
{
    public RecordAutoNestedProfile()
    {
        CreateMap<SourceAddress, RecordDestAddress>();

        CreateMap<SourceWithNav, RecordDestWithNav>();
    }
}

/// <summary>Explicit collection For binding into a record (List element).</summary>
public class RecordCollectionProfile : MapperProfile
{
    public RecordCollectionProfile()
    {
        CreateMap<SourceItem, RecordDestItem>();

        CreateMap<SourceWithCollection, RecordDestWithCollection>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Name, s => s.Name)
            .For(d => d.Items, s => s.Items);
    }
}

/// <summary>AutoMap resolving a record collection member automatically.</summary>
public class RecordAutoCollectionProfile : MapperProfile
{
    public RecordAutoCollectionProfile()
    {
        CreateMap<SourceItem, RecordDestItem>();

        CreateMap<SourceWithCollection, RecordDestWithCollection>();
    }
}

/// <summary>Collection For binding materializing to an array constructor parameter.</summary>
public class RecordArrayProfile : MapperProfile
{
    public RecordArrayProfile()
    {
        CreateMap<SourceItem, RecordDestItem>();

        CreateMap<SourceForArray, RecordDestWithArray>()
            .For(d => d.Id, s => s.Id)
            .For(d => d.Items, s => s.Items);
    }
}

/// <summary>
/// Record with both constructor parameters and an extra init-only member —
/// the constructor handles Id/Name, the extra member is applied via object initializer.
/// </summary>
public class RecordMixedProfile : MapperProfile
{
    public RecordMixedProfile()
    {
        CreateMap<SourceSimple, RecordDestMixed>()
            .For(d => d.Extra, s => s.Name + "!");
    }
}

/// <summary>Ignored constructor parameter falls back to its default value.</summary>
public class RecordIgnoreProfile : MapperProfile
{
    public RecordIgnoreProfile()
    {
        CreateMap<SourceSimple, RecordDestPartial>()
            .Ignore(d => d.Ignored);
    }
}

// ═══════════════════════════════════════════════════════════════════
// Conversion / runtime-type / cycle regressions
// ═══════════════════════════════════════════════════════════════════

/// <summary>AutoMap with an int source property feeding an int? destination property.</summary>
public class NullableLiftProfile : MapperProfile
{
    public NullableLiftProfile()
    {
        CreateMap<SourceNumeric, DestNumericNullable>();
    }
}

/// <summary>Two different source types mapping to the same destination type.</summary>
public class HeterogeneousProfile : MapperProfile
{
    public HeterogeneousProfile()
    {
        CreateMap<SourceSimple, DestSimple>();

        CreateMap<SourceSimpleAlt, DestSimple>()
            .For(d => d.Name, s => s.Name + "-alt");
    }
}

/// <summary>AutoMap applying implicit numeric widening (int → long).</summary>
public class AutoMapWideningProfile : MapperProfile
{
    public AutoMapWideningProfile()
    {
        CreateMap<SourceNumeric, DestNumericWide>();
    }
}

/// <summary>AutoMap must NOT apply narrowing (long → int).</summary>
public class AutoMapNarrowingProfile : MapperProfile
{
    public AutoMapNarrowingProfile()
    {
        CreateMap<SourceWideValue, DestNarrowValue>();
    }
}

/// <summary>AutoMap converting enum properties to strings.</summary>
public class EnumToStringProfile : MapperProfile
{
    public EnumToStringProfile()
    {
        CreateMap<SourceWithEnum, DestWithEnumAsString>();
    }
}

/// <summary>AutoMap converting between two mirrored enum types by value.</summary>
public class EnumToEnumProfile : MapperProfile
{
    public EnumToEnumProfile()
    {
        CreateMap<SourceWithEnum, DestWithEnumAsEnum>();
    }
}

/// <summary>Self-referencing mapping — configuration must fail with a clear error.</summary>
public class CyclicProfile : MapperProfile
{
    public CyclicProfile()
    {
        CreateMap<SourceCyclic, DestCyclic>();
    }
}

/// <summary>Mutually-referencing mappings (Customer → Order → Customer).</summary>
public class MutualCyclicProfile : MapperProfile
{
    public MutualCyclicProfile()
    {
        CreateMap<SourceCustomer, DestCustomer>();
        CreateMap<SourceOrder, DestOrder>();
    }
}

/// <summary>
/// Same shape as <see cref="MutualCyclicProfile"/> but the cycle is broken by
/// ignoring the back-reference, as the cycle error message suggests.
/// </summary>
public class MutualCyclicResolvedProfile : MapperProfile
{
    public MutualCyclicResolvedProfile()
    {
        CreateMap<SourceCustomer, DestCustomer>();

        CreateMap<SourceOrder, DestOrder>()
            .Ignore(d => d.Customer);
    }
}

/// <summary>
/// Mixes explicit nested / collection For bindings with AutoMap to verify that
/// explicit bindings take precedence over auto-resolution.
/// </summary>
public class AutoMapExplicitOverrideProfile : MapperProfile
{
    public AutoMapExplicitOverrideProfile()
    {
        CreateMap<SourceAddress, DestAddress>();

        CreateMap<SourceItem, DestItem>();

        CreateMap<SourceWithNav, DestWithNav>()
            .For(d => d.Address, s => s.Address);

        CreateMap<SourceWithCollection, DestWithCollection>()
            .For(d => d.Items, s => s.Items);
    }
}

