# Commerce & Compliance — Locked Decisions

Authorized by the Unity developer, 12 August 2026. This is the authority for the commerce,
economy, content and compliance work. **Do not reopen these without a new decision from the
client** — they were locked specifically to stop the build stalling on product questions.

Endpoint shapes live in `@ApiReference.md`. This file records *why*, and what is deliberately
absent.

---

## Locked

| Question | Decision |
|---|---|
| Real money | **Out of scope.** Soft/in-game currency only. |
| Apple IAP / Google Play Billing | Out of scope. No receipts, no payment processor. |
| Subscriptions | Out of scope. No billing periods, renewals or cancellation. |
| `Offer.price` | An amount of a server-authoritative soft currency, e.g. `coins`. |
| Account deletion | **Immediate hard delete.** No grace period, no pending state, no cancel. |
| Currency authority | **Server.** The client never states an amount. |
| Reward amounts | From configurable `RewardRule` rows, evaluated against validated progress. |
| Reward trigger | `POST /api/progress/attempts` only — no generic "client can earn" endpoint. |
| Region eligibility | **Deferred.** No reliable region source exists on the account. |
| Localized text | Unity owns it. The backend returns stable keys only. |
| Cosmetics | Opaque stable client references. No backend cosmetic catalogue. |
| Addressables hosting | Unity/CDN. The manifest is metadata + entitlement gating only. |
| Refunds | No customer-facing API. The ledger must support future corrections. |
| Admin UI | Optional. Must not block the API. |
| Error envelope | Additive for new endpoints; existing auth/curriculum contract untouched. |

## Explicitly out of scope

Real-money payments · IAP · subscriptions · refund APIs · premium-currency purchase ·
leaderboards · achievements · friends/social · daily challenges · ranked persistence ·
match-result persistence · notifications · analytics ingestion · backend cosmetic catalogue ·
backend Addressables hosting · region/geo eligibility · live-ops admin platform.

If one of these looks necessary mid-build, implement the smallest compatible thing and flag it —
do not stop.

---

## Three standing architectural constraints

These came from the client as future-proofing and outrank convenience.

**1. Keep domains separated.** Not one `CommerceService` that accretes rewards, then friends, then
leaderboards. Current split: `Auth`, `Users`, `Progress`, `Economy`, `Commerce`, `Content`,
`Rewards`. The wallet lives in `Economy`, the shop in `Commerce` — deliberately not the same
namespace, because they have different lifetimes and different owners.

**2. The ledger must stay extensible.** `CurrencyLedgerEntry` carries type, source, reference and
free-form metadata, and `CurrencyTransactionType` already names values with no producer yet
(`AchievementReward`, `DailyReward`, `EventReward`, `LeaderboardReward`, `Refund`, `Reversal`).
A later module records against *this* ledger rather than growing a parallel one — several
disagreeing coin counters is the failure mode being designed against. Corrections are new
compensating entries; history is never mutated.

**3. Do not couple to Unity's vocabulary.** The backend thinks in `GameId`, `ProductId`, `Grant`,
`Currency`, `Reward`, `Entitlement`, `ContentPack`. Unity maps those onto `GameKey`, Addressable
addresses, cosmetic ids and prefabs. Where a mapping is genuinely needed (the content manifest),
it is explicit and one-directional.

---

## Deviations from the authorized contract

The contract says to implement the smallest compatible thing and flag it rather than stop. These
are what that produced. **None changes a client-facing shape that was specified** — the reward
tables are internal, and the response additions are additive.

**1. `RewardRule` grants many currencies, not one.** The contract sketches `RewardRule` with a
single `currency` + `amount`. Implemented instead as `RewardRule` → `RewardRuleGrant[]`
(currency, amount), and `RewardTransaction` → `RewardTransactionLine[]` to match.

*Why:* "finishing a lesson gives 10 coins and 2 gems" is one rule with one policy. With currency on
the rule it takes two rules, which means two independent cooldowns and daily counters that drift
apart, and a state where half the reward has been paid and recorded as complete. As a child table
the whole payout is one transaction under one cooldown, and adding a currency to a live reward is
an `INSERT` rather than a migration. `RewardRule` never appears on the wire, so this breaks nothing
on the client.

**2. Cooldown and daily limit are constraints, not repeat policies.** `RepeatPolicy` is `ONCE` or
`EVERY_TIME`; `cooldownSeconds` and `dailyLimit` are independent optional modifiers. Modelling them
as policy values would make them mutually exclusive, and "at most 5 a day, no more than one every
10 minutes" is an ordinary rule. Sending either on a `ONCE` rule is refused rather than ignored.

**3. `GAME_COMPLETED` is not implemented.** The contract offers it as an example event type.
Nothing in this backend defines what completing a *game* means — a game is a presentation mode over
the shared curriculum, and there is no state that says a player has finished one. Per "do not invent
mechanics unless the client provides the authoritative event", only the three lesson events exist.
Adding one later is a rule row plus the code that raises it.

**4. `POST /api/progress/attempts` accepts an optional `requestId`.** The contract mandates reward
idempotency but the attempt endpoint had no submission identity, so a retry after a lost response
was indistinguishable from genuinely replaying the lesson and an `EVERY_TIME` rule paid twice.
Added as an **optional** field with a server-derived fallback, so the existing client keeps working
unchanged and becomes exactly-once the moment Unity starts sending it. **This is the one item that
wants the Unity developer's agreement**, since it needs a client change to take effect. A test
documents the current behaviour without it.

**5. Rewards compose rather than override.** A global rule and a lesson-specific rule for the same
event both fire and both pay. The contract does not say which; independence was chosen because it
needs no precedence rules and expresses base-plus-bonus directly.

### Phase 05 — products and entitlements

**6. A product's grants freeze once anyone owns it.** The contract requires that an entitlement stay
resolvable after delisting, and specifies the chain
`Entitlement → Product → ProductGrant → COSMETIC + cosmetic_id` — i.e. resolved through the product
on read, not snapshotted onto the entitlement. That makes the grant rows load-bearing for everyone
who already owns the product, so editing them is refused with `PRODUCT_GRANTS_LOCKED` once
`ownerCount > 0`. Renaming, retiring and metadata stay editable. The alternative — snapshotting
grants onto each entitlement — would have contradicted the contract's stated chain.

**7. An entitlement is unique per (user, product).** Ownership is a boolean, so a repeat grant
returns the existing row rather than creating a second. This also makes granting idempotent at the
database level, which is what phase 07's retried purchase will collide with. It rules out
*consumable* products, which is why no `CURRENCY` grant kind exists: real-money and premium-currency
purchases are out of scope, so a product that grants spendable currency has no coherent meaning
here. Adding consumables later needs more than an enum value.

**8. `CONTENT_PACK` exists alongside `COSMETIC`.** The contract only names `COSMETIC`, but §15's
manifest gates content packs on entitlement, so the kind is needed to express that at all. Unlike a
reward event with no producer, this is not dead config — the entitlement is granted and the
reference preserved exactly like a cosmetic; only the manifest that reads it is unbuilt.

**9. `Product` carries `key` and `name`, which the contract's sketch does not.** `key` is a stable
handle so seed data and migrations refer to products without hard-coding GUIDs, exactly as
`Currency.Key` does; `name` is admin-facing only. Neither goes to the client — `productId` remains
the wire identity — so no client shape changed.

### Phase 05 revision — product kinds (2026-08-14, requested by the Unity dev)

The product module was reshaped to a schema the Unity developer specified directly. The wire
contract is unchanged: `grants[]` still carries `{ kind, reference, quantity }` and `productId` is
still the client identity. What moved is underneath.

**10. Kind moved from the grant to the product, and from an enum to a table.** `GrantKind` is gone;
`ProductKind (Id, Name, Description)` replaces it and `Products.ProductKindId` is a required foreign
key. A product is therefore *one* kind of thing and all its grants report it — a bundle mixing
categories is now two products. The `kind` field on the wire is unchanged and is resolved from the
product on read.

The cost is real and worth stating: the vocabulary is admin-authored text now, so **the backend can
no longer validate it**. `ProductKindName.ToWire` normalises whatever was typed to
`SCREAMING_SNAKE` (`Content Pack`, `content-pack` and `ContentPack` all become `CONTENT_PACK`) and
names that would collide on that form are refused, which is what stops one token meaning two things.
But a kind named something Unity does not recognise is undetectable here — exactly like `reference`
already was. The migration seeds `Cosmetic` and `Content Pack` so the existing vocabulary survives.

**11. `Products.Metadata`, `CreatedAtUtc` and `UpdatedAtUtc` were dropped**, and `Description` and
`ImageUrl` added, per the specified column list. Nothing on the wire used any of the three, so no
client shape changed — but the audit timestamps are gone and the migration does not preserve them.

**12. Grants are authored through their own endpoints, not inline on the product.** They are a table
with their own CRUD now (`/api/admin/product-grants`), so `POST /api/admin/products` no longer
accepts a `grants[]` array and **a product can exist granting nothing**. Phase 05 refused that;
enforcing "at least one" is impossible when the two are authored separately. Consequence for phase
07: **purchase must refuse to sell a product with no grants**, or it will charge for an empty
entitlement.

**13. Products and grants can now be deleted — but only while nobody owns them.** The contract says
ownership must outlive the shop, and phase 05 read that as "never delete". Delete now exists because
it was asked for, and is refused with `PRODUCT_OWNED` / `PRODUCT_GRANTS_LOCKED` once `ownerCount > 0`
— which preserves the actual requirement, since only an *owned* product stranding its owners breaks
it. The `Entitlements → Products` FK stays `Restrict`, so the database enforces it independently of
the service. `PRODUCT_KIND_IN_USE` does the same for a kind a product still references.

**14. Re-categorising an owned product is allowed**, unlike editing its grants. Kind changes how the
client *reads* the references; it does not change *which* references the owner receives. Since the
migration had to collapse per-grant kinds down to one per product, fixing a miscategorised product
after it has sold has to remain possible.

**15. Shop text is per language, in `*Translations` tables.** Products and kinds follow the
curriculum tree: the parent row carries no display name, a name is required for **every** configured
language, and `translations[]` replaces the whole set on update. `Products` therefore lost `Name`
and `Description` and `ProductKinds` lost `Description`; the migration copies each existing value
into a row for every language so nothing predating the change is left half-translated.

The exception is **`ProductKind.Name`, which stays untranslated** — it is the source of the `kind`
token, and `COSMETIC` has to mean the same thing to an Arabic client as to an English one. A kind
therefore has three names: the machine name, the normalised token derived from it, and a label per
language that never leaves the admin surface.

Product text still does not reach Unity — `products[]` in the offers response is `productId` +
`grants[]`. **Offer** text does: an offer is the thing a student reads in the shop, and nothing else
could carry "50% off, this week only". Resolved from `preferred_language` on the token.

### Phases 06 and 07 — offers and purchase (2026-08-16)

**16. `productIds` is a list, not the contract's single `productId`.** The sketch gives each offer
one product, which cannot express a bundle. An offer sells one or more products through an
`OfferProducts` join table, and buying it grants **all** of them as one purchase, one price and one
transaction. `metadata` was dropped in the same change and replaced by per-language `name` and
`description`, and `currencyId` was added alongside `currency` — the **key** stays on the wire
because that is what `GET /api/commerce/balances` reports and therefore what the client can compare
against.

**17. Stored availability has two values; the reported vocabulary has four.** `Offers.Availability`
is only `AVAILABLE`/`UNAVAILABLE` — the admin's switch. `EXPIRED` and `PURCHASE_LIMIT_REACHED` are
derived per request from the server clock and the caller's history, because they depend on when you
ask and who is asking. Unavailable reports as `DISABLED`, which is §14's token for it.

**18. `canPurchase` deliberately ignores the balance.** Too few coins is a refusal at the moment of
buying, not a reason to grey an offer out — a student should see what they are saving towards, and
the client already knows both numbers. Everything else (disabled, expired, limit reached) does grey
it out, with a reason key.

**19. Refusals are recorded as transactions.** `PurchaseTransactions` holds refused attempts as well
as completed ones, with the reason key. "It took my coins and gave me nothing" is answerable, and an
offer nobody can afford is visible as a run of refusals instead of as silence. The only refusal with
no row is `OFFER_NOT_FOUND` — there is nothing to record it against.

**20. `requestId` carries purchase idempotency** (§9 of the authorized contract calls it mandatory).
Unique `(UserId, RequestId)`, enforced by the database rather than by a read-then-write, so
simultaneous retries cannot both land. It is **optional on the wire** — omitted, the server generates
one and the call is simply a fresh purchase.

**Only a completed purchase replays; a refusal is re-evaluated** (corrected 2026-08-16). The index is
therefore *filtered* to completed rows. Replaying refusals was the original reading of "return the
same purchase result", and it was wrong in the way that matters: idempotency protects a **charge**,
and a refusal made none. A player told `INSUFFICIENT_BALANCE`, who tops up and taps buy again, is not
asking "did it go through" — they are asking again, having fixed the thing that stopped them.
Replaying the stale "no" meant that with any fixed `requestId` (Swagger's default value, or a client
that reuses one) the purchase could never succeed again. The half that must not move is preserved: a
completed purchase still replays forever, even after the offer expires.

**21. Buying something already fully owned is refused, not charged.** Entitlements are unique per
(user, product), so a second purchase of a durable product hands over nothing. A bundle where only
*some* products are owned still completes — the rest are worth paying for.

⚠ **Consequence: a `purchaseLimit` above 1 is currently unreachable.** Already-owned fires before the
limit can be approached. `purchaseLimit: 1` is what "buy once" means today; limits above 1 only
become meaningful once something consumable exists to sell, which needs more than a new column (see
deviation 7).

**22. There is no update-offer endpoint** (requested 2026-08-16). The offer surface is `POST`, `GET`,
`DELETE` and buy — nothing else. Deleting is separately refused once any transaction references the
offer (`OFFER_PURCHASED`), refusals included.

⚠ **Together these leave no way to change or withdraw an offer that has been transacted against**,
and a single refused attempt is enough to lock it. A mispriced offer someone tried to buy stays on
sale at that price permanently. **`expiresAtUtc` is the only remaining way an offer stops selling**,
so anything that might need withdrawing should be authored with one.

The narrow fix, if this bites: a single-field `PATCH /api/admin/offers/{id}/availability`. It
reopens no re-pricing and rewrites no history — it only flips the admin's on/off switch, which is
what "take it off sale" actually needs. Not built, because the endpoint set was specified
explicitly.

---

## Required test coverage

Not a coverage percentage — these specific behaviours:

- [x] Account deletion authorization
- [x] Account deletion cleanup
- [x] Account deletion idempotency
- [x] Purchase success
- [x] Purchase insufficient balance
- [x] Purchase eligibility failure
- [x] Purchase idempotency
- [x] Concurrent duplicate purchase
- [x] Atomic balance + entitlement grant
- [x] Reward idempotency
- [x] Reward server-side amount calculation
- [x] Balance reconciliation
- [x] Entitlement survives offer removal
- [x] Offer expiration
- [x] Offer eligibility
- [ ] Export scope

The purchase concurrency and idempotency tests are the ones that matter most.

---

## Build order and status

| # | Phase | Status |
|---|---|---|
| 01 | Error envelope, test project, shared infrastructure | **Done** |
| 02 | `DELETE /api/users/me` | **Done** |
| 03 | Currency, balances, ledger | **Done** |
| 04 | `RewardRule`, `RewardTransaction`, wired to progress attempts | **Done** |
| 05 | Product, ProductGrant, Entitlement, `GET /api/commerce/entitlements` | **Done** |
| 06 | Offer, eligibility vocabulary, `GET /api/commerce/offers` | **Done** |
| 07 | `POST /api/commerce/purchase` | **Done** |
| 08 | `GET /api/content/manifest` | Not started |
| 09 | `GET /api/users/me/export` | Not started |
| 10 | `GET /api/users/me/profile` | Not started (`GET /api/time` **done**) |

## Definition of done

Unity can run this end to end:

```
auth → progress → server-validated reward → authoritative balance
     → offers → purchase → atomic deduction → entitlement → ledger → content manifest
```

plus: deletion removes all data and revokes sessions; reconnect reconciles the local wallet from
`GET /api/commerce/balances`; and retrying a purchase with the same `requestId` returns the
original outcome without charging twice.
