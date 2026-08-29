using System.Text;
using System.Text.Json;
using PrRag.Application.DTOs;

namespace PrRag.DataGenerator;

public static class Program
{
    private static readonly Random _rng = new();

    private static readonly (string Code, string Name)[] Suppliers =
    {
        ("SUP000001", "Acme Industrial Supply"),
        ("SUP000002", "Beta Components Ltd"),
        ("SUP000003", "Gamma Metals Corp"),
        ("SUP000004", "Delta Tools & Machinery"),
        ("SUP000005", "Epsilon Chemicals"),
        ("SUP000006", "Zeta Packaging"),
        ("SUP000007", "Eta Electronics"),
        ("SUP000008", "Theta Construction Materials"),
        ("SUP000009", "Iota Office Solutions"),
        ("SUP000010", "Kappa Logistics Equipment"),
    };

    private static readonly (string Item, string ItemName, string[] Descriptions)[] Catalog =
    {
        ("ITM-00000000000000000001", "Hydraulic Pump", new[] { "High pressure hydraulic pump for extrusion line maintenance.", "Replacement hydraulic pump rated for continuous industrial operation.", "Hydraulic pump unit with variable displacement control." }),
        ("ITM-00000000000000000002", "Steel Sheet", new[] { "Cold rolled steel sheet, 2mm thickness, industrial grade.", "Stainless steel sheet for fabrication, corrosion resistant.", "Galvanized steel sheet roll for structural use." }),
        ("ITM-00000000000000000003", "Ball Bearings", new[] { "Precision ball bearings, sealed, for conveyor systems.", "Roller ball bearings, high load rating, maintenance kit.", "Ball bearing assembly with lubrication sealed in." }),
        ("ITM-00000000000000000004", "Control PLC", new[] { "Programmable logic controller with 32 digital inputs and outputs.", "Industrial PLC module for automated line control, ethernet enabled.", "PLC controller unit with expansion I/O." }),
        ("ITM-00000000000000000005", "Safety Gloves", new[] { "Protective cut-resistant gloves, latex coated palm.", "Industrial safety gloves, oil resistant, pack of 12.", "Heat resistant safety gloves for foundry operations." }),
        ("ITM-00000000000000000006", "Copper Wiring", new[] { "Insulated copper wiring, 10 AWG, 100m spool.", "Flexible copper cable, high conductivity, electrical grade.", "Copper wire bundle for panel wiring." }),
        ("ITM-00000000000000000007", "Hydraulic Oil", new[] { "Mineral hydraulic oil, ISO 46 viscosity, 20L drum.", "Synthetic hydraulic fluid for high pressure systems.", "Hydraulic oil additive package for wear protection." }),
        ("ITM-00000000000000000008", "Packaging Cartons", new[] { "Corrugated carton boxes, 400x300x250mm, double wall.", "Foldable shipping cartons, tear resistant, 100 units.", "Cardboard packaging cartons for export shipment." }),
        ("ITM-00000000000000000009", "LED Lighting", new[] { "Industrial LED bay lighting, 200W, waterproof.", "LED work lights for machine inspection stations.", "Explosion proof LED light fixtures for hazardous areas." }),
        ("ITM-00000000000000000010", "Cutting Tools", new[] { "Carbide cutting insert sets for CNC machining.", "High speed steel end mills, assorted sizes.", "Cutting tool holders for lathe operations." }),
    };

    public static async Task<int> Main(string[] args)
    {
        var outputPath = args.Length > 0 ? args[0] : "data/purchase.json";
        const int targetCount = 3000;

        Console.WriteLine($"Generating {targetCount} synthetic purchase requisitions...");

        var records = new List<PurchaseRequisitionImport>(targetCount);
        var start = DateTime.UtcNow.AddMonths(-18);

        for (var i = 0; i < targetCount; i++)
        {
            var supplier = Suppliers[_rng.Next(Suppliers.Length)];
            var catalogItem = Catalog[_rng.Next(Catalog.Length)];
            var description = catalogItem.Descriptions[_rng.Next(catalogItem.Descriptions.Length)];

            var date = start.AddSeconds(_rng.Next(0, (int)(DateTime.UtcNow - start).TotalSeconds));

            records.Add(new PurchaseRequisitionImport
            {
                PurchaseRequisition = $"PR{i:00000000}",
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                Item = catalogItem.Item,
                ItemName = catalogItem.ItemName,
                Description = description,
            });
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(records, options);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8);
        Console.WriteLine($"Wrote {targetCount} records to {Path.GetFullPath(outputPath)}.");
        return 0;
    }
}
