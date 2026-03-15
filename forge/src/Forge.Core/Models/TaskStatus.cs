using NpgsqlTypes;

namespace Forge.Core.Models;

public enum ForgeTaskStatus
{
    [PgName("pending")] Pending,
    [PgName("claimed")] Claimed,
    [PgName("pr_opened")] PrOpened,
    [PgName("in_review")] InReview,
    [PgName("addressing_review")] AddressingReview,
    [PgName("done")] Done,
    [PgName("failed")] Failed
}
