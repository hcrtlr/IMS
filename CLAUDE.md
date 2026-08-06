# Memory

## Me
[Name/role not yet captured] — a "stajyer" (intern) working on the **Inventory Management System** project.

## People
| Who | Role |
|-----|------|
| _(none named in the source document — it's a technical spec, not correspondence)_ |

## Terms
| Term | Meaning |
|------|---------|
| WMS | Warehouse Management System — the full enterprise system this project is a scaled-down slice of |
| SKU | Stock Keeping Unit — unique product identifier code, unique per Account |
| UOM | Unit of Measure — Each / Pack / Case / Pallet conversion hierarchy |
| LPN | License Plate (Number) — ID for a pallet, case, or tote; can be nested (Pallet → Case → ...) |
| FEFO | First Expired, First Out — picking strategy prioritizing soonest-expiring lot |
| FIFO | First In, First Out — picking strategy prioritizing oldest-received stock |
| CRUD | Create, Read, Update, Delete |
| EF Core | Entity Framework Core — the specified ORM |
| Faz | Turkish for "Phase" — used in the project's 6-stage delivery plan (Faz 1–6) |
| Stajyer Projesi | Turkish for "Intern Project" — the doc's own title for this assignment |

## Projects
| Name | What |
|------|------|
| **Inventory Management System (IMS)** | Intern project: scaled-down WMS covering warehouse structure, item master, inventory tracking, inbound/outbound flow, and data groundwork for future slotting/picking algorithms. Stack: .NET 8 Web API, PostgreSQL, EF Core, Swagger, Docker, xUnit/NUnit, FluentValidation. Layered: Domain / Application / Infrastructure / API. See `memory/projects/inventory-management-system.md` for full detail. |

## Preferences
- Source material is in Turkish; domain terms mix Turkish and English — keep both when relevant.
- No `dashboard.html` available in this environment (built for Cowork's persistent workspace) — task tracking happens via `TASKS.md` directly in chat for now.
- This chat environment resets storage between conversations — download `TASKS.md`/`CLAUDE.md` to carry state forward, or re-upload next session.
