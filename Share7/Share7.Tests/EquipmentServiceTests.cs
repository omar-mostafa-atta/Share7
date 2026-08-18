using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Equipment.Models;
using Share7.Domain.Commerce;
using Share7.Domain.Equipment;
using Share7.Infrastructure.Equipment;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class EquipmentServiceTests
{
    private readonly SqlServerFixture _fixture;

    public EquipmentServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    private static EquipmentService Service(ApplicationDbContext context, bool enforceOwnership = false) =>
        new(context, Options.Create(new EquipmentOptions { EnforceOwnership = enforceOwnership }));

    private static UpdateEquipmentRequest Request(
        string? bodyType = null,
        List<EquipmentSlotInput>? equipped = null) =>
        new() { BodyType = bodyType, Equipped = equipped };

    private static EquipmentSlotInput Slot(string slot, string cosmetic, string? color = null) =>
        new() { SlotKey = slot, CosmeticKey = cosmetic, ColorKey = color };

    private static Task<List<UserEquipment>> RowsOfAsync(ApplicationDbContext context, Guid userId) =>
        context.Equipments.AsNoTracking().Where(e => e.UserId == userId).ToListAsync();

    // -----------------------------------------------------------------------------------------
    // storage shape — one row per equipped item
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_equipped_items_are_stored_as_two_rows()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await Service(context).ReplaceAsync(userId, Request("Male", [
            Slot("Head", "Dia_Hel", "Red"),
            Slot("Body", "gold_Shield", "Blue")
        ]));

        await using var check = _fixture.CreateContext();
        var rows = await RowsOfAsync(check, userId);

        Assert.Equal(2, rows.Count);

        var head = rows.Single(r => r.SlotKey == "Head");
        Assert.Equal("Dia_Hel", head.CosmeticKey);
        Assert.Equal("Red", head.ColorKey);
        Assert.Equal(BodyType.Male, head.BodyType);

        var body = rows.Single(r => r.SlotKey == "Body");
        Assert.Equal("gold_Shield", body.CosmeticKey);
        Assert.Equal("Blue", body.ColorKey);

        // Per-player values repeat on every row rather than living somewhere else.
        Assert.Single(rows.Select(r => r.BodyType).Distinct());
        Assert.Single(rows.Select(r => r.UpdatedAtUtc).Distinct());
    }

    [Fact]
    public async Task Re_equipping_a_slot_updates_its_row_instead_of_adding_one()
    {
        // The stated rule: the same user cannot hold the same slotKey on two rows.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = Service(context);

        await service.ReplaceAsync(userId, Request(equipped: [Slot("Head", "Dia_Hel", "Red")]));

        await using var afterFirst = _fixture.CreateContext();
        var originalId = (await RowsOfAsync(afterFirst, userId)).Single().Id;

        await service.ReplaceAsync(userId, Request(equipped: [Slot("Head", "Iron_Hel", "Green")]));

        await using var check = _fixture.CreateContext();
        var rows = await RowsOfAsync(check, userId);

        var row = Assert.Single(rows);
        Assert.Equal(originalId, row.Id);          // same row, updated in place
        Assert.Equal("Iron_Hel", row.CosmeticKey);
        Assert.Equal("Green", row.ColorKey);
    }

    [Fact]
    public async Task Dropping_a_slot_removes_only_that_row()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = Service(context);

        await service.ReplaceAsync(userId, Request(equipped: [
            Slot("Head", "Dia_Hel"),
            Slot("Body", "gold_Shield")
        ]));

        await using var afterFirst = _fixture.CreateContext();
        var bodyId = (await RowsOfAsync(afterFirst, userId)).Single(r => r.SlotKey == "Body").Id;

        await service.ReplaceAsync(userId, Request(equipped: [Slot("Body", "gold_Shield")]));

        await using var check = _fixture.CreateContext();
        var row = Assert.Single(await RowsOfAsync(check, userId));

        Assert.Equal("Body", row.SlotKey);
        Assert.Equal(bodyId, row.Id);              // the surviving slot kept its row
    }

    [Fact]
    public async Task The_database_refuses_a_second_row_for_the_same_user_and_slot()
    {
        // The service never tries this, but the guarantee should not depend on the service.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await Service(context).ReplaceAsync(userId, Request(equipped: [Slot("Head", "Dia_Hel")]));

        await using var direct = _fixture.CreateContext();
        direct.Equipments.Add(new UserEquipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SlotKey = "Head",
            CosmeticKey = "Sneaked_In",
            UpdatedAtUtc = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync());
    }

    // -----------------------------------------------------------------------------------------
    // updatedAtUtc — the one rule that strips every existing player if it is wrong
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Never_saved_returns_defaults_with_a_null_timestamp()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await Service(context).GetAsync(userId);

        Assert.Null(result.UpdatedAtUtc);
        Assert.Empty(result.Equipped);
        Assert.Empty(result.Colors);
        Assert.Equal(BodyType.Male, result.BodyType);
    }

    [Fact]
    public async Task Deliberately_wearing_nothing_is_distinguishable_from_never_saved()
    {
        // The pair the no-items row exists for: identical empty lists, different meanings, and the
        // only thing separating them is whether updatedAtUtc is null.
        await using var context = _fixture.CreateContext();
        var neverDressed = await TestData.CreateUserAsync(context);
        var undressed = await TestData.CreateUserAsync(context);

        Assert.True((await Service(context).ReplaceAsync(undressed, Request(equipped: []))).Succeeded);

        await using var check = _fixture.CreateContext();
        var never = await Service(check).GetAsync(neverDressed);
        var bare = await Service(check).GetAsync(undressed);

        Assert.Null(never.UpdatedAtUtc);
        Assert.NotNull(bare.UpdatedAtUtc);

        Assert.Empty(never.Equipped);
        Assert.Empty(bare.Equipped);
        Assert.Empty(never.Colors);
        Assert.Empty(bare.Colors);
    }

    [Fact]
    public async Task Wearing_nothing_stores_exactly_one_row_with_a_null_slot()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await Service(context).ReplaceAsync(userId, Request("Female", []));

        await using var check = _fixture.CreateContext();
        var row = Assert.Single(await RowsOfAsync(check, userId));

        Assert.Null(row.SlotKey);
        Assert.Null(row.CosmeticKey);
        Assert.Null(row.ColorKey);
        Assert.True(row.IsNoItemsRow);
        // Body type survives having nothing on, which it could not if the row did not exist.
        Assert.Equal(BodyType.Female, row.BodyType);
    }

    [Fact]
    public async Task Undressing_then_dressing_again_clears_the_no_items_row()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = Service(context);

        await service.ReplaceAsync(userId, Request(equipped: [Slot("Head", "Dia_Hel")]));
        await service.ReplaceAsync(userId, Request(equipped: []));
        await service.ReplaceAsync(userId, Request(equipped: [Slot("Head", "Dia_Hel")]));

        await using var check = _fixture.CreateContext();
        var rows = await RowsOfAsync(check, userId);

        var row = Assert.Single(rows);
        Assert.Equal("Head", row.SlotKey);
        Assert.DoesNotContain(rows, r => r.IsNoItemsRow);
    }

    [Fact]
    public async Task Undressing_twice_does_not_accumulate_marker_rows()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = Service(context);

        await service.ReplaceAsync(userId, Request(equipped: []));
        await service.ReplaceAsync(userId, Request(equipped: []));
        await service.ReplaceAsync(userId, Request(equipped: []));

        await using var check = _fixture.CreateContext();
        Assert.Single(await RowsOfAsync(check, userId));
    }

    [Fact]
    public async Task Stored_timestamp_comes_back_as_utc_so_it_serialises_with_a_z()
    {
        // SQL Server returns datetime2 as Unspecified, which the serialiser writes without a
        // trailing Z; a client doing a naive parse would then shift it by its own offset.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await Service(context).ReplaceAsync(userId, Request(equipped: [Slot("head", "hat_wizard")]));

        await using var check = _fixture.CreateContext();
        var result = await Service(check).GetAsync(userId);

        Assert.Equal(DateTimeKind.Utc, result.UpdatedAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task Saving_again_moves_the_timestamp_forward_on_every_row()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = Service(context);

        var first = await service.ReplaceAsync(userId, Request(equipped: [Slot("head", "hat_a")]));
        await Task.Delay(10);
        var second = await service.ReplaceAsync(userId, Request(equipped: [
            Slot("head", "hat_a"),          // untouched slot still gets the new stamp
            Slot("body", "coat_b")
        ]));

        Assert.True(second.Value!.UpdatedAtUtc > first.Value!.UpdatedAtUtc);

        await using var check = _fixture.CreateContext();
        var rows = await RowsOfAsync(check, userId);
        Assert.Single(rows.Select(r => r.UpdatedAtUtc).Distinct());
    }

    // -----------------------------------------------------------------------------------------
    // the response projection
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Rows_project_into_the_two_list_response_shape()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await Service(context).ReplaceAsync(userId, Request("Female", [
            Slot("Body", "Armor_gold", "Hexa"),
            Slot("Head", "Dia_Hat", "Bronze")
        ]));

        await using var check = _fixture.CreateContext();
        var result = await Service(check).GetAsync(userId);

        Assert.Equal(BodyType.Female, result.BodyType);

        Assert.Equal(
            [("Body", "Armor_gold"), ("Head", "Dia_Hat")],
            result.Equipped.Select(e => (e.SlotKey, e.CosmeticKey)));

        Assert.Equal(
            [("Armor_gold", "Hexa"), ("Dia_Hat", "Bronze")],
            result.Colors.Select(c => (c.CosmeticKey, c.ColorKey)));
    }

    [Fact]
    public async Task Every_colour_names_a_cosmetic_that_is_actually_equipped()
    {
        // The invariant the nested request shape exists to guarantee — the old flat colors[] let a
        // client colour something it was not wearing.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await Service(context).ReplaceAsync(userId, Request(equipped: [
            Slot("Body", "Armor_gold", "Hexa"),
            Slot("Head", "Dia_Hat", "Bronze")
        ]));

        await using var check = _fixture.CreateContext();
        var result = await Service(check).GetAsync(userId);

        var worn = result.Equipped.Select(e => e.CosmeticKey).ToHashSet();
        Assert.All(result.Colors, c => Assert.Contains(c.CosmeticKey, worn));
    }

    [Fact]
    public async Task An_item_with_no_colour_is_absent_from_the_colours_list()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await Service(context).ReplaceAsync(userId, Request(equipped: [
            Slot("Head", "Dia_Hat", "Bronze"),
            Slot("Body", "Armor_plain")          // worn, no colour picked
        ]));

        await using var check = _fixture.CreateContext();
        var result = await Service(check).GetAsync(userId);

        Assert.Equal(2, result.Equipped.Count);
        var color = Assert.Single(result.Colors);
        Assert.Equal("Dia_Hat", color.CosmeticKey);
    }

    [Fact]
    public async Task Unequipping_an_item_discards_its_colour()
    {
        // Direct consequence of colour living on the item row: a dye is not remembered for next
        // time. Deliberate, and pinned here so it cannot regress silently.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = Service(context);

        await service.ReplaceAsync(userId, Request(equipped: [Slot("Body", "jacketbomber", "crimson")]));
        await service.ReplaceAsync(userId, Request(equipped: [Slot("Head", "hat_wizard")]));

        await using var check = _fixture.CreateContext();
        var result = await Service(check).GetAsync(userId);

        Assert.Empty(result.Colors);
        Assert.DoesNotContain(await RowsOfAsync(check, userId), r => r.ColorKey == "crimson");
    }

    [Fact]
    public async Task A_save_replaces_the_whole_outfit_rather_than_merging()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = Service(context);

        await service.ReplaceAsync(userId, Request(equipped: [
            Slot("head", "hat_a", "red"),
            Slot("body", "coat_b", "blue")
        ]));

        // Taking everything off has to actually take everything off — if an empty list were read
        // as "no change", a player could never undress.
        await service.ReplaceAsync(userId, Request(equipped: []));

        await using var check = _fixture.CreateContext();
        var stored = await Service(check).GetAsync(userId);

        Assert.Empty(stored.Equipped);
        Assert.Empty(stored.Colors);
        Assert.NotNull(stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task Two_users_outfits_are_independent()
    {
        await using var context = _fixture.CreateContext();
        var first = await TestData.CreateUserAsync(context);
        var second = await TestData.CreateUserAsync(context);
        var service = Service(context);

        await service.ReplaceAsync(first, Request("Male", [Slot("head", "hat_a")]));
        await service.ReplaceAsync(second, Request("Female", [Slot("head", "hat_b")]));

        await using var check = _fixture.CreateContext();
        var one = await Service(check).GetAsync(first);
        var two = await Service(check).GetAsync(second);

        Assert.Equal("hat_a", one.Equipped.Single().CosmeticKey);
        Assert.Equal("hat_b", two.Equipped.Single().CosmeticKey);
        Assert.Equal(BodyType.Male, one.BodyType);
        Assert.Equal(BodyType.Female, two.BodyType);
    }

    // -----------------------------------------------------------------------------------------
    // body type
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, BodyType.Male)]
    [InlineData("", BodyType.Male)]
    [InlineData("Male", BodyType.Male)]
    [InlineData("male", BodyType.Male)]
    [InlineData("MALE", BodyType.Male)]
    [InlineData("Female", BodyType.Female)]
    [InlineData("female", BodyType.Female)]
    public async Task Body_type_parses_tolerantly_and_defaults_to_male(string? sent, BodyType expected)
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await Service(context).ReplaceAsync(userId, Request(sent, [Slot("head", "hat_a")]));

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Value!.BodyType);
    }

    [Fact]
    public async Task Unknown_body_type_is_rejected_rather_than_silently_defaulted()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        AssertUnprocessable(
            await Service(context).ReplaceAsync(userId, Request("Alien")),
            ApiErrors.EquipmentInvalid);
    }

    // -----------------------------------------------------------------------------------------
    // caps and key rules — 422
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Too_many_equipped_entries_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var tooMany = Enumerable.Range(0, EquipmentLimits.MaxEquipped + 1)
            .Select(i => Slot($"slot{i}", $"cos{i}"))
            .ToList();

        AssertUnprocessable(
            await Service(context).ReplaceAsync(userId, Request(equipped: tooMany)),
            ApiErrors.EquipmentInvalid);
    }

    [Fact]
    public async Task Exactly_the_cap_is_allowed()
    {
        // Guards the off-by-one: the limit is inclusive.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var atCap = Enumerable.Range(0, EquipmentLimits.MaxEquipped)
            .Select(i => Slot($"slot{i}", $"cos{i}"))
            .ToList();

        var result = await Service(context).ReplaceAsync(userId, Request(equipped: atCap));

        Assert.True(result.Succeeded);
        Assert.Equal(EquipmentLimits.MaxEquipped, result.Value!.Equipped.Count);

        await using var check = _fixture.CreateContext();
        Assert.Equal(EquipmentLimits.MaxEquipped, (await RowsOfAsync(check, userId)).Count);
    }

    [Fact]
    public async Task The_same_slot_twice_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        AssertUnprocessable(
            await Service(context).ReplaceAsync(userId, Request(equipped: [
                Slot("head", "hat_a"), Slot("head", "hat_b")
            ])),
            ApiErrors.EquipmentInvalid);
    }

    [Fact]
    public async Task The_same_slot_in_different_casing_is_still_the_same_slot()
    {
        // Storing both would need two rows where the unique index allows one, so this has to be
        // caught before it reaches the database.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        AssertUnprocessable(
            await Service(context).ReplaceAsync(userId, Request(equipped: [
                Slot("Head", "hat_a"), Slot("head", "hat_b")
            ])),
            ApiErrors.EquipmentInvalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("<script>")]
    [InlineData("emoji_\U0001F600")]
    [InlineData("semi;colon")]
    public async Task Malformed_keys_are_rejected(string key)
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        AssertUnprocessable(
            await Service(context).ReplaceAsync(userId, Request(equipped: [Slot(key, "hat_a")])),
            ApiErrors.EquipmentInvalid);
    }

    [Fact]
    public async Task A_malformed_colour_key_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        AssertUnprocessable(
            await Service(context).ReplaceAsync(userId, Request(equipped: [
                Slot("head", "hat_a", "not a colour")
            ])),
            ApiErrors.EquipmentInvalid);
    }

    [Fact]
    public async Task An_over_long_key_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var tooLong = new string('a', EquipmentLimits.MaxKeyLength + 1);

        AssertUnprocessable(
            await Service(context).ReplaceAsync(userId, Request(equipped: [Slot("head", tooLong)])),
            ApiErrors.EquipmentInvalid);
    }

    [Theory]
    [InlineData("hat_wizard")]      // underscore — every example key in the spec uses one
    [InlineData("jacket.bomber")]
    [InlineData("hat-wizard")]
    [InlineData("Dia_Hel")]
    [InlineData("cos123")]
    public async Task Allowed_key_shapes_are_accepted(string key)
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await Service(context).ReplaceAsync(userId, Request(equipped: [Slot("head", key)]));

        Assert.True(result.Succeeded, $"'{key}' should be a valid cosmetic key");
        Assert.Equal(key, result.Value!.Equipped.Single().CosmeticKey);
    }

    [Fact]
    public async Task An_unknown_cosmetic_key_is_stored_rather_than_refused()
    {
        // No backend catalogue by decision — refusing unknown keys would stop content shipping
        // ahead of a backend deploy.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await Service(context).ReplaceAsync(userId, Request(equipped: [
            Slot("slot_that_does_not_exist", "cosmetic_invented_yesterday")
        ]));

        Assert.True(result.Succeeded);
        Assert.Equal("cosmetic_invented_yesterday", result.Value!.Equipped.Single().CosmeticKey);
    }

    [Fact]
    public async Task A_rejected_save_leaves_the_previous_outfit_intact()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = Service(context);

        await service.ReplaceAsync(userId, Request(equipped: [Slot("head", "hat_good", "red")]));
        await service.ReplaceAsync(userId, Request(equipped: [Slot("head", "bad key!")]));

        await using var check = _fixture.CreateContext();
        var stored = await Service(check).GetAsync(userId);

        Assert.Equal("hat_good", stored.Equipped.Single().CosmeticKey);
        Assert.Equal("red", stored.Colors.Single().ColorKey);
    }

    // -----------------------------------------------------------------------------------------
    // ownership — built, off by default
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Ownership_is_not_checked_while_enforcement_is_off()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await Service(context, enforceOwnership: false)
            .ReplaceAsync(userId, Request(equipped: [Slot("head", "hat_nobody_owns")]));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Enforcement_on_rejects_a_cosmetic_the_account_does_not_own()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        AssertUnprocessable(
            await Service(context, enforceOwnership: true)
                .ReplaceAsync(userId, Request(equipped: [Slot("head", "hat_nobody_owns")])),
            ApiErrors.EquipmentNotOwned);
    }

    [Fact]
    public async Task Enforcement_on_allows_a_cosmetic_reached_through_an_entitlement()
    {
        // Ownership resolves the same chain a purchase writes: entitlement → product → grant
        // reference, which is the client-side cosmetic id.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync(grants: [new GrantSpecification("hat_owned")]);

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductId = product.Id,
            GrantedAtUtc = DateTime.UtcNow,
            Source = EntitlementSource.AdminGrant
        });
        await context.SaveChangesAsync();

        var result = await Service(context, enforceOwnership: true)
            .ReplaceAsync(userId, Request(equipped: [Slot("head", "hat_owned", "crimson")]));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Enforcement_on_never_ownership_checks_the_colour()
    {
        // A colour is a property of a cosmetic, not a separately owned thing.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync(grants: [new GrantSpecification("hat_owned")]);

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductId = product.Id,
            GrantedAtUtc = DateTime.UtcNow,
            Source = EntitlementSource.AdminGrant
        });
        await context.SaveChangesAsync();

        var result = await Service(context, enforceOwnership: true)
            .ReplaceAsync(userId, Request(equipped: [Slot("head", "hat_owned", "colour_nobody_owns")]));

        Assert.True(result.Succeeded);
        Assert.Equal("colour_nobody_owns", result.Value!.Colors.Single().ColorKey);
    }

    // -----------------------------------------------------------------------------------------
    // account deletion
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_the_account_takes_every_outfit_row_with_it()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        await Service(context).ReplaceAsync(userId, Request(equipped: [
            Slot("head", "hat_a"), Slot("body", "coat_b")
        ]));

        var user = await context.Users.SingleAsync(u => u.Id == userId);
        context.Users.Remove(user);
        await context.SaveChangesAsync();

        await using var check = _fixture.CreateContext();
        Assert.Empty(await RowsOfAsync(check, userId));
    }

    private static void AssertUnprocessable(ServiceResult<EquipmentDto> result, ApiErrorCode expected)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.Unprocessable, result.ErrorKind);
        Assert.Equal(expected.Code, result.Error?.Code);
    }
}
