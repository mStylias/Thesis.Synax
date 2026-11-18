using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.IntegrationTests.EntityScanning
{
    internal sealed class RootScannedEntity : IEntity;
}

namespace Synax.EntityFrameworkCore.IntegrationTests.EntityScanning.Child
{
    internal sealed class ChildScannedEntity : IEntity;
}

namespace Synax.EntityFrameworkCore.IntegrationTests.EntityScanningOther
{
    internal sealed class OutsideScannedEntity : IEntity;
}

namespace Synax.EntityFrameworkCore.IntegrationTests.OtherEntityScanning
{
    internal sealed class OtherScannedEntity : IEntity;
}
