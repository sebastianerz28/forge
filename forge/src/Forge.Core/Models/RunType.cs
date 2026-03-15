using NpgsqlTypes;

namespace Forge.Core.Models;

public enum RunType
{
    [PgName("initial")] Initial,
    [PgName("review_address")] ReviewAddress
}
