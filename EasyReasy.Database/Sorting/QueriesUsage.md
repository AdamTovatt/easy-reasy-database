← [Back to main domain overview](../../README.md)

# Queries Usage Guide

## For Model/DTO Developers (Marking Properties as Sortable)

### Basic Usage
Mark properties that should be sortable with the `[Sortable]` attribute:

```csharp
public class CustomerWithCoverCounts
{
    [Sortable]
    public int Id { get; set; }

    [Sortable]
    public string FirstName { get; set; }

    [Sortable]
    public string LastName { get; set; }
}
```

### Custom SQL Column Names
If your property name doesn't match the database column name, specify it explicitly:

```csharp
[Sortable("total_cover_count")]
public int TotalCoverCount { get; set; }
```

### Default Sort Column
Mark one property as the default sort column (used when no sort is specified):

```csharp
[Sortable(isDefault: true)]
public DateTime SignUpTime { get; set; }
```

### Combined Options
You can combine custom column name and default sort:

```csharp
[Sortable("sign_up_time", isDefault: true)]
public DateTime SignUpTime { get; set; }
```

### Auto-Generated Column Names
If no custom column name is specified, the property name is automatically converted to snake_case:
- `FirstName` → `first_name`
- `ActiveCoverCount` → `active_cover_count`
- `ExternalID` → `external_id`

## For Repository Developers (Building ORDER BY Clauses)

### Using OrderBy Helper (Recommended)
The `OrderBy.Create<TDto>()` method handles default sorting and validation:

```csharp
public async Task<List<CustomerWithCoverCounts>> GetAllWithCoverCountsAsync(
    SortColumn? sortColumn = null,
    SortOrder sortOrder = SortOrder.Descending,
    IDbSession? session = null)
{
    string query = $@"
        SELECT ...
        FROM customer c
        {OrderBy.Create<CustomerWithCoverCounts>(sortColumn, sortOrder)}
        OFFSET @offset
        LIMIT @perPage";
    
    // ... execute query
}
```

**Benefits:**
- Automatically uses default sort column if `sortColumn` is null
- Returns empty string if no sorting is available
- Validates that the sort column is valid (via `SortColumn.Create`)

### Creating SortColumn from Property Name
Validate and create a `SortColumn` from a property name string:

```csharp
// Validates property exists and is marked as sortable
SortColumn sortColumn = SortColumn.Create<CustomerWithCoverCounts>("FirstName");
// sortColumn.SqlColumnName will be "first_name"
```

**Throws `ArgumentException` if:**
- Property doesn't exist
- Property isn't marked with `[Sortable]`

### Using SortableFieldHelper Directly
For more control, use `SortableFieldHelper` methods:

```csharp
// Get SQL column name for a property
string sqlColumn = SortableFieldHelper.GetSqlColumnName<CustomerWithCoverCounts>("FirstName");
// Returns "first_name" (or custom column name if specified)

// Get default sort column
string? defaultColumn = SortableFieldHelper.GetDefaultSortColumn<CustomerWithCoverCounts>();
// Returns "sign_up_time" if SignUpTime is marked as default

// Build ORDER BY clause manually
string orderBy = SortableFieldHelper.BuildOrderByClause("first_name", SortOrder.Ascending);
// Returns "ORDER BY first_name ASC"
```

### Getting All Sortable Fields
Get a list of all sortable property names (useful for API documentation):

```csharp
List<string> sortableFields = SortableFieldHelper.GetSortableFields<CustomerWithCoverCounts>();
// Returns: ["Id", "FirstName", "LastName", "Email", "SignUpTime", ...]
```

## For Service/API Developers (Using Sort Parameters)

### Accepting Sort Parameters
Repository methods should accept `SortColumn?` and `SortOrder` parameters:

```csharp
public async Task<List<CustomerWithCoverCounts>> GetAllWithCoverCountsAsync(
    int page,
    int perPage,
    SortColumn? sortColumn = null,
    SortOrder sortOrder = SortOrder.Descending,
    IDbSession? session = null)
```

### Creating SortColumn from User Input
When receiving sort requests from API endpoints, validate and create `SortColumn`:

```csharp
// In your service/controller
string? sortField = request.SortField; // e.g., "FirstName" or "firstName"

SortColumn? sortColumn = null;
if (!string.IsNullOrEmpty(sortField))
{
    try
    {
        sortColumn = SortColumn.Create<CustomerWithCoverCounts>(sortField);
    }
    catch (ArgumentException)
    {
        // Handle invalid sort field
        throw new BadRequestException($"Invalid sort field: {sortField}");
    }
}

SortOrder sortOrder = request.SortOrder == "asc" 
    ? SortOrder.Ascending 
    : SortOrder.Descending;

List<CustomerWithCoverCounts> results = await _customerRepository
    .GetAllWithCoverCountsAsync(page, perPage, sortColumn, sortOrder);
```

### Getting Available Sort Fields
Expose available sort fields to API consumers:

```csharp
// In your API endpoint
[HttpGet("sortable-fields")]
public IActionResult GetSortableFields()
{
    List<string> fields = SortableFieldHelper.GetSortableFields<CustomerWithCoverCounts>();
    return Ok(fields);
}
```

## Design Principles

- **All sortable properties must be marked with `[Sortable]`** - ensures explicit control over what can be sorted
- **Property names are case-insensitive** - `"FirstName"` and `"firstName"`both work
- **Auto snake_case conversion** - property names automatically convert to database column names
- **Custom column names override auto-conversion** - use when property name doesn't match database
- **Default sort is optional** - if no default is specified and no sort column provided, no ORDER BY is added
- **SortColumn validates at creation time** - invalid properties throw exceptions immediately
- **OrderBy handles null gracefully** - returns empty string if no sorting is possible

