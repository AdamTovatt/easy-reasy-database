← [Back to overview](../README.md)

# EasyReasy.Database.Testing.PruneTool

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Testing.PruneTool.svg)](https://www.nuget.org/packages/EasyReasy.Database.Testing.PruneTool/)

`prune-test-databases` — a dotnet tool that reclaims the per-checkout test databases (and optional scratch directories) of git checkouts that no longer exist.

Test harnesses using [EasyReasy.Database.Testing.Npgsql](../EasyReasy.Database.Testing.Npgsql/README.md) derive a database per checkout and stamp it with an ownership marker recording the checkout path. Deleting a worktree leaves its database behind; this tool finds and drops those leftovers safely.

## Installation

```bash
dotnet tool install --global EasyReasy.Database.Testing.PruneTool
```

## Usage

```bash
# List what would be dropped, drop nothing:
prune-test-databases --prefix myproject_test_ --marker myproject-test-checkout:

# Actually drop:
prune-test-databases --prefix myproject_test_ --marker myproject-test-checkout: --yes

# With per-checkout scratch directories (e.g. an e2e harness's published backends):
prune-test-databases --prefix myproject_test_ --marker myproject-test-checkout: \
    --scratch-root /tmp/myproject-e2e --yes
```

## Options

| Option | Required | Default | Meaning |
|--------|----------|---------|---------|
| `--prefix` | yes | — | Derived-database name prefix, e.g. `myproject_test_`. |
| `--marker` | yes | — | Ownership-marker prefix, e.g. `myproject-test-checkout:`. |
| `--connection-string` | no | `Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres` | Reaches a non-default cluster. The tool always operates from the `postgres` maintenance database regardless of the database this names. |
| `--scratch-root` | no | none | Root of per-checkout scratch directories. Repeatable. |
| `--scratch-marker-file` | no | `checkout` | Name of the marker file inside a scratch tree. See [Scratch trees](#scratch-trees). |
| `--yes` | no | off | Perform the drops instead of only listing them. |

Neither `--prefix` nor `--marker` may be blank. An empty marker would be a prefix of *every* database comment in the cluster, which is exactly the widening the marker exists to prevent, so the tool refuses it rather than sweeping on it.

## Safety rules

Nothing is dropped unless BOTH hold:

1. the database name has the exact derived shape — the prefix followed by 8 lowercase hex characters. A hand-made database like `myproject_test_1418` never qualifies, whatever it contains;
2. the database carries the `<marker><checkout-path>` ownership comment the harness stamps, AND that path no longer exists. The harness stamps ONLY when it derived the name — never when the name was pinned via the pin environment variable — so a database someone else named cannot acquire the marker even if its name happens to look derived.

Without `--yes` the tool only lists what it would do, and always exits 0 on a clean run.

### Scratch trees

Scratch trees are reclaimed by two passes, because there are two ways for one to be orphaned:

- **Alongside a dropped database.** When a database qualifies under the rules above, the tree at `<scratch-root>/<the 8 hash characters its name ends in>` is removed with it. No marker file is needed or read: the *database's* marker has already proved that checkout gone, and the hash names the one tree that belongs to it.
- **On its own marker file.** A checkout that PINNED its database name still writes a scratch tree under its derived hash, and no database row points at it — so it can only be found by the tree's own marker file (`--scratch-marker-file`, default `checkout`), which holds the same `<marker><checkout-path>` text as the database comment. A tree qualifies when that path no longer exists.

The second pass is what `--scratch-marker-file` configures, and it only finds trees whose marker file your harness writes. Nothing in these packages writes one — the trees are your e2e harness's artifacts, so it owns creating them and stamping them. If it does not, drop `--scratch-root` and the first pass alone will still clean up alongside each dropped database.

## Caveats

A checkout is judged gone by whether its recorded directory exists *right now*, so a path that is temporarily unreachable — an unmounted external volume, a stale network mount — reads as deleted. The dry-run default is the mitigation: every verdict is printed as `drop <db> <- <path> (gone)` before `--yes` does anything, so read the listing before applying it. If your checkouts live on removable media, confirm the volume is mounted first.
