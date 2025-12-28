using Catalog.Database;
using Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Extensions
{
    public static class DatabaseSeedExtensions
    {
        // Конфигурация для количества данных
        private const int CATALOGS_COUNT = 100;  // Общее количество каталогов
        private const int PRODUCTS_PER_CATALOG = 100;  // Продуктов на каталог
        private const int MAX_CATALOG_DEPTH = 3;  // Максимальная глубина вложенности

        public static void SeedCatalogs(this IApplicationBuilder app)
        {
            using IServiceScope scope = app.ApplicationServices.CreateScope();
            using ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!context.Catalogs.Any())
            {
                var catalogs = GenerateCatalogs();
                
                context.Catalogs.AddRange(catalogs);
                context.SaveChanges();
                
                Console.WriteLine($"✅ Seeded {catalogs.Count} catalogs");
            }
        }

        public static void SeedProducts(this IApplicationBuilder app)
        {
            using IServiceScope scope = app.ApplicationServices.CreateScope();
            using ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!context.Products.Any() && context.Catalogs.Any())
            {
                var allCatalogs = context.Catalogs.ToList();
                var products = GenerateProducts(allCatalogs);

                // Добавляем продукты пакетами для производительности
                const int batchSize = 1000;
                for (int i = 0; i < products.Count; i += batchSize)
                {
                    var batch = products.Skip(i).Take(batchSize).ToList();
                    context.Products.AddRange(batch);
                    context.SaveChanges();
                    Console.WriteLine($"✅ Seeded batch {i / batchSize + 1}: {batch.Count} products");
                }

                Console.WriteLine($"✅ Total products seeded: {products.Count}");
            }
        }

        private static List<Catalog.Domain.Catalogs.Catalog> GenerateCatalogs()
        {
            var catalogs = new List<Catalog.Domain.Catalogs.Catalog>();
            var random = new Random(42); // Фиксированный seed для воспроизводимости

            // Основные категории (уровень 1)
            var mainCategories = new[]
            {
                ("Electronics", "Electronic devices and gadgets"),
                ("Fashion", "Clothing and accessories"),
                ("Home & Garden", "Home improvement and gardening"),
                ("Sports & Outdoors", "Sports equipment and outdoor gear"),
                ("Books & Media", "Books, music, and movies"),
                ("Toys & Games", "Toys and gaming"),
                ("Health & Beauty", "Health and beauty products"),
                ("Automotive", "Car parts and accessories"),
                ("Food & Beverages", "Food and drinks"),
                ("Office Supplies", "Office and school supplies")
            };

            // Создаем основные категории
            var mainCatalogsList = new List<Catalog.Domain.Catalogs.Catalog>();
            foreach (var (name, description) in mainCategories)
            {
                var catalog = Catalog.Domain.Catalogs.Catalog.Create(
                    name,
                    description,
                    null,
                    GenerateSlug(name)
                );
                catalogs.Add(catalog);
                mainCatalogsList.Add(catalog);
            }

            int currentCount = mainCategories.Length;
            int remainingCatalogs = CATALOGS_COUNT - mainCategories.Length;
            int catalogsPerMain = remainingCatalogs / mainCategories.Length;

            // Создаем подкатегории
            var level2Catalogs = new List<Catalog.Domain.Catalogs.Catalog>();
            
            foreach (var mainCatalog in mainCatalogsList)
            {
                if (currentCount >= CATALOGS_COUNT) break;
                
                // Подкатегории уровня 2
                int subCatCount = Math.Min(catalogsPerMain / 2, CATALOGS_COUNT - currentCount);
                
                for (int i = 0; i < subCatCount; i++)
                {
                    var subCatalogName = $"{mainCatalog.Name} Category {i + 1}";
                    var subCatalog = Catalog.Domain.Catalogs.Catalog.Create(
                        subCatalogName,
                        $"Sub-category of {mainCatalog.Name}",
                        mainCatalog,
                        GenerateSlug(subCatalogName)
                    );
                    catalogs.Add(subCatalog);
                    level2Catalogs.Add(subCatalog);
                    currentCount++;
                    
                    if (currentCount >= CATALOGS_COUNT) break;
                }
            }

            // Создаем подкатегории уровня 3
            foreach (var level2Catalog in level2Catalogs)
            {
                if (currentCount >= CATALOGS_COUNT) break;
                
                // Не все подкатегории получают вложенные категории
                if (random.Next(0, 2) == 0) continue;
                
                int subSubCount = Math.Min(random.Next(1, 4), CATALOGS_COUNT - currentCount);
                
                for (int i = 0; i < subSubCount; i++)
                {
                    var subSubCatalogName = $"{level2Catalog.Name} Type {i + 1}";
                    var subSubCatalog = Catalog.Domain.Catalogs.Catalog.Create(
                        subSubCatalogName,
                        $"Specific type in {level2Catalog.Name}",
                        level2Catalog,
                        GenerateSlug(subSubCatalogName)
                    );
                    catalogs.Add(subSubCatalog);
                    currentCount++;
                    
                    if (currentCount >= CATALOGS_COUNT) break;
                }
            }

            Console.WriteLine($"Generated {catalogs.Count} catalogs (Target: {CATALOGS_COUNT})");
            return catalogs;
        }

        private static List<Product> GenerateProducts(List<Catalog.Domain.Catalogs.Catalog> catalogs)
        {
            var products = new List<Product>();
            var random = new Random(42);

            var productPrefixes = new[]
            {
                "Premium", "Professional", "Classic", "Modern", "Deluxe",
                "Standard", "Essential", "Ultimate", "Advanced", "Basic"
            };

            var productTypes = new[]
            {
                "Item", "Product", "Device", "Tool", "Accessory",
                "Kit", "Set", "Bundle", "Package", "Collection"
            };

            var currencies = new[] { "USD", "EUR", "GBP" };

            foreach (var catalog in catalogs)
            {
                int productsToCreate = random.Next(PRODUCTS_PER_CATALOG / 2, PRODUCTS_PER_CATALOG * 2);

                for (int i = 0; i < productsToCreate; i++)
                {
                    var prefix = productPrefixes[random.Next(productPrefixes.Length)];
                    var type = productTypes[random.Next(productTypes.Length)];
                    var productName = $"{prefix} {catalog.Name} {type} #{i + 1}";
                    
                    var description = $"High-quality {type.ToLower()} from {catalog.Name} category. " +
                                    $"Perfect for your needs. Product code: {catalog.Slug}-{i:D4}";

                    var price = Math.Round((decimal)(random.NextDouble() * 990 + 10), 2);
                    var currency = currencies[random.Next(currencies.Length)];
                    var stockQuantity = random.Next(0, 200);

                    var product = Product.Create(
                        productName,
                        description,
                        Money.Create(price, currency),
                        catalog.Id,
                        stockQuantity,
                        null // MerchantId, если нужен - можно добавить
                    );

                    products.Add(product);
                }
            }

            return products;
        }

        private static string GenerateSlug(string input)
        {
            return input
                .ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("&", "and")
                .Replace("'", "")
                .Replace(",", "");
        }

        // Дополнительный метод для очистки данных (полезно для повторного тестирования)
        public static void ClearDatabase(this IApplicationBuilder app)
        {
            using IServiceScope scope = app.ApplicationServices.CreateScope();
            using ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Console.WriteLine("🗑️  Clearing database...");
            
            context.Products.RemoveRange(context.Products);
            context.Catalogs.RemoveRange(context.Catalogs);
            context.SaveChanges();
            
            Console.WriteLine("✅ Database cleared");
        }

        // Метод для вывода статистики
        public static void PrintDatabaseStats(this IApplicationBuilder app)
        {
            using IServiceScope scope = app.ApplicationServices.CreateScope();
            using ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var catalogCount = context.Catalogs.Count();
            var productCount = context.Products.Count();
            var avgProductsPerCatalog = catalogCount > 0 ? productCount / (decimal)catalogCount : 0;

            Console.WriteLine("\n📊 Database Statistics:");
            Console.WriteLine($"   Catalogs: {catalogCount}");
            Console.WriteLine($"   Products: {productCount}");
            Console.WriteLine($"   Avg Products/Catalog: {avgProductsPerCatalog:F2}");
            Console.WriteLine();
        }
    }
}