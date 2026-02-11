---
name: siam-ski-company
description: "Use this skill whenever questions arise about the company, business context, branding, product domain, or customer-facing language. Triggers on: references to the company, business requirements, product catalog decisions, customer personas, booking flows, or any question about why the platform exists and who it serves. Siam Ski is a Bangkok-based ski gear e-commerce and ski tourism company operating in Thailand."
---

# Siam Ski — Company Context

## About

**Siam Ski** is a ski gear e-commerce and ski tourism company headquartered in **Bangkok, Thailand**. This eShop repository is the company's **online store and booking platform**.

## What We Do

| Business Line | Description |
|---|---|
| Ski Gear E-Commerce | Sell ski equipment, apparel, and accessories to customers in Thailand and Southeast Asia |
| Ski Tourism | Offer ski travel packages, resort bookings, and guided ski trip experiences |

## Why This Matters for Code

When writing code for this platform, keep in mind:

1. **Product catalog** — Items include ski gear (skis, boots, poles, helmets, goggles, apparel) and bookable tourism packages (trips, resort stays, lessons). These are different product categories with different purchase flows.
2. **Dual model** — The platform is both an **e-commerce store** (physical goods with shipping) and a **booking platform** (tourism packages with dates, availability, and reservations). Code should respect this distinction.
3. **Target market** — Customers are based in Thailand and Southeast Asia. Consider:
   - Thai Baht (THB) as the primary currency
   - Thai language support alongside English
   - Southeast Asian shipping logistics
   - Seasonal demand aligned with Northern Hemisphere ski seasons (Dec–Mar)
4. **Brand voice** — Siam Ski positions itself as making skiing accessible to a tropical market. Tone is adventurous, aspirational, and approachable.

## Domain Terminology

Use these terms consistently in code, APIs, and UI:

| Domain Term | Meaning | Example Usage |
|---|---|---|
| Catalog Item | A purchasable product (gear or package) | `CatalogItem`, `CatalogType` |
| Ski Gear | Physical products — equipment & apparel | Catalog type for shippable goods |
| Trip Package | A bookable ski tourism experience | Catalog type with dates and availability |
| Booking | A reservation for a trip package | Distinct from a product order |
| Order | A purchase of physical ski gear | Standard e-commerce order flow |

## Key Considerations for Development

- **Currency**: Default to THB. Support multi-currency display where relevant.
- **Timezone**: Bangkok is UTC+7 (`Asia/Bangkok`). Use this for booking availability, order timestamps displayed to customers.
- **Localization**: Support `th-TH` and `en-US` locales at minimum.
- **Shipping**: Domestic Thailand shipping is the primary case; international shipping to neighboring countries is secondary.
- **Seasonality**: Ski season (Dec–Mar) drives peak traffic and promotions. The platform should support seasonal campaigns and flash sales.
