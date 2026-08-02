using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedGradeLookupData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Grades",
                columns: new[] { "Id", "NameAr", "NameEn", "Order" },
                values: new object[,]
                {
                    { new Guid("2b004d42-ca9c-4823-ac5b-1cb59cba0467"), "الصف الرابع", "Grade 4", 4 },
                    { new Guid("38111b66-de42-4adc-b20c-464b1dd2a9d1"), "الصف الثاني عشر", "Grade 12", 12 },
                    { new Guid("52c97299-9356-46d5-87e9-fc1ac17d5c14"), "الصف الثالث", "Grade 3", 3 },
                    { new Guid("76e10c16-da74-4731-8818-448262946b70"), "الصف السادس", "Grade 6", 6 },
                    { new Guid("895f300d-21b4-4167-afc7-6c06677abf0e"), "الصف التاسع", "Grade 9", 9 },
                    { new Guid("b14d62e5-56ee-42a4-916a-5554d811f72f"), "الصف الخامس", "Grade 5", 5 },
                    { new Guid("c2287d7b-e111-45ef-ac10-a3669c8a5eeb"), "الصف الحادي عشر", "Grade 11", 11 },
                    { new Guid("c3f4e414-cde4-4929-a235-d4f09b5f748d"), "الصف السابع", "Grade 7", 7 },
                    { new Guid("cc203a51-cc8c-4814-ae5e-587c9903e17c"), "الصف الثاني", "Grade 2", 2 },
                    { new Guid("de6e690a-9bbf-4e32-afea-7e36942f7957"), "الصف الثامن", "Grade 8", 8 },
                    { new Guid("ee0bc8b5-de4a-4a25-afaa-cfdec9e9e27a"), "الصف الأول", "Grade 1", 1 },
                    { new Guid("f4313c32-260c-4e55-8877-c68b9d5ae33d"), "الصف العاشر", "Grade 10", 10 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("2b004d42-ca9c-4823-ac5b-1cb59cba0467"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("38111b66-de42-4adc-b20c-464b1dd2a9d1"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("52c97299-9356-46d5-87e9-fc1ac17d5c14"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("76e10c16-da74-4731-8818-448262946b70"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("895f300d-21b4-4167-afc7-6c06677abf0e"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("b14d62e5-56ee-42a4-916a-5554d811f72f"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c2287d7b-e111-45ef-ac10-a3669c8a5eeb"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c3f4e414-cde4-4929-a235-d4f09b5f748d"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("cc203a51-cc8c-4814-ae5e-587c9903e17c"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("de6e690a-9bbf-4e32-afea-7e36942f7957"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("ee0bc8b5-de4a-4a25-afaa-cfdec9e9e27a"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("f4313c32-260c-4e55-8877-c68b9d5ae33d"));
        }
    }
}
