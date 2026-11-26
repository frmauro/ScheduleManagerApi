using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Scheduling.Infrastructure.Persistence;

/// <summary>Converte nomes de colunas, chaves, índices e FKs para snake_case (sem dependência extra — research R-13).</summary>
internal static class SnakeCaseNaming
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null)
            {
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(table, entity.GetSchema());
            foreach (var property in entity.GetProperties())
            {
                var current = property.GetColumnName(storeObject);
                if (current is not null && current != "xmin")
                {
                    property.SetColumnName(ToSnakeCase(current));
                }
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName() ?? string.Empty));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName() ?? string.Empty));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? string.Empty));
            }
        }
    }

    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var previousIsLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                var previousIsUpper = i > 0 && char.IsUpper(name[i - 1]);
                if (i > 0 && name[i - 1] != '_' && (previousIsLowerOrDigit || (previousIsUpper && nextIsLower)))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
