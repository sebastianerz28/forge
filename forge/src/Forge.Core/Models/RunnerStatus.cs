using NpgsqlTypes;

namespace Forge.Core.Models;

public enum RunnerStatus
{
    [PgName("active")] Active,
    [PgName("dead")] Dead
}
