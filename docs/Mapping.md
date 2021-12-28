# Mapping

Echidna can automatically map data coming from your database to a variety of common C# types.

## POCOs, Records, and Anonymous Types



When binding to a POCO, it is allowed to have `DbDataReader` columns that go unused OR to have optional POCO/Record properties that go unbound, but **not both at once**. This helps avoid missed bindings due to typos.

## Dictionaries

Sometimes you won't know which columns you'll need to query until runtime. In this case, you might want to map to a dictionary type. Echidna supports the following types:
* `Dictionary<string, TValue>`
* `IDictionary<string, TValue>`
* `IReadOnlyDictionary<string, TValue>`
* `ExpandoObject`

`TValue` can be any type but must be mappable from all column types in the query.

Where possible, dictionaries are constructed with `StringComparer.OrdinalIgnoreCase` as their `IEqualityComparer`.

## ValueTuples

Unlike with POCOs, `ValueTuple` element names are not available for reflection at runtime. Because of this, `ValueTuple` mapping is performed based on column position only. For example:

```C#
var maps = db.ReadSingle<(string A, int B)>($"SELECT 'a' AS i, 2 AS j");
var doesNotMap = db.ReadSingle<(string A, int B)>($"SELECT 1 AS a, 'a' AS b");
```

**ValueTuples with non-scalar fields can be used to populate multiple objects from a single DbDataReader row.** For example:

```C#
var customersAndOrders = db.Read<(Customer Customer, Order Order)>($@"
	SELECT c.*, o.*
	FROM customers c
	JOIN orders o ON o.customerId = c.id"
);
```

As with other `ValueTuple` mappings, order is important when binding. The `Customer` field will bind to as many columns as it can starting from column 0. Once it reaches a column it can't bind to, `Order` will start binding from that column. For a successful binding, each `ValueTuple` field must bind to at least one column.

## Scalars

"Scalar" values are types that are mapped from a single database column (e. g. `int`). Scalar mappings occur as part of mapping composite types or when executing queries that return a single column.

When mapping scalars, Echidna allows for some deviation from exact type matching so long as the deviation is "safe":

* Primitive numeric types can be mapped to each other so long as there is no loss of information. For example, a column whose type is `INT` can always be mapped to `long`. A column whose type is `BIG_INT` can be mapped to `int`, but if the mapper encounters a value outside the range of `int` an exception will be thrown.
* Similarly, boolean columns can be mapped to numeric values 0 and 1, and numeric values 0 and 1 can be mapped to `bool` values.
* Similarly, numeric columns can be mapped to Enum values so long as the value is defined for the enumeration
* Similarly, `DateOnly` and `TimeOnly` values can be mapped from `DateTime` and `TimeSpan` values so long as the former has no time of day components and the latter is within the range of `TimeOnly`.
* If a C# implicit conversion operator exists between the types, a mapping is allowed using that operator
* Any type can be mapped to `object`
* Any type `T` or `T?` can be mapped to `V?` so long as `T` can be mapped to `V`


