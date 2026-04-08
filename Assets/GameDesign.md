# DEEP RESCUE — Game Design Document

## High Concept
A real-time sci-fi drone management game where you command a fleet of customizable
rescue drones through a procedurally generated hex-room facility. Discover survivors,
neutralize hazards, and extract everyone before time runs out.

---

## 1. SETTING
A damaged sci-fi facility (space station / colony / research base). The hex rooms
represent sealed compartments connected by corridors. The player views the map from
an RTS aerial perspective and never controls a character directly — only drones.

---

## 2. CORE LOOP (real-time, classic RTS)
```
Deploy drones → Scout the unknown → Discover survivors & hazards
    → Triage (who's critical?) → Route drones to assist
    → Neutralize hazards → Extract survivors → Repeat
```
All happening simultaneously. The player juggles multiple crises at once.

---

## 3. DRONE CHASSIS SYSTEM (customizable)

### 3.1 Chassis (base frame — determines size, speed, battery)
| Chassis    | Speed | Battery | Slots | Notes                      |
|------------|-------|---------|-------|----------------------------|
| Wasp       | Fast  | Small   | 1     | Cheap, expendable          |
| Hornet     | Med   | Medium  | 2     | Workhorse                  |
| Titan      | Slow  | Large   | 4     | Expensive, tanky           |
| Mosquito   | VFast | Tiny    | 0     | Scout-only, auto-maps      |

### 3.2 Modules (slotted into chassis)
| Module         | Slots | Function                                  |
|----------------|-------|-------------------------------------------|
| Scanner        | 1     | Reveals fog of war in larger radius        |
| Cargo Bay      | 1     | Carry supplies (O₂, meds, power cells)     |
| Survivor Pod   | 2     | Carry one survivor for extraction          |
| Fire Suppressor| 1     | Neutralize fire hazard in a room           |
| Rad Scrubber   | 1     | Neutralize radiation in a room             |
| Pump Module    | 1     | Drain flooding from a room                 |
| Repair Arm     | 1     | Clear debris / repair doors                |
| Shield Gen     | 1     | Drone takes less damage from hazards       |
| Battery Ext.   | 1     | +50% battery life                          |
| Booster        | 1     | +40% movement speed                        |

### 3.3 Customization Flow
Before each mission: pick chassis → fill module slots → deploy.
During mission: drones can return to base to swap modules (costs time).

---

## 4. HEX ROOM SYSTEM

### 4.1 Room States
- **Unknown** — fog of war, must be scouted
- **Clear** — safe, traversable
- **Hazardous** — has an active hazard (see 4.2)
- **Blocked** — debris blocks the corridor, needs Repair Arm
- **Collapsed** — permanently destroyed, impassable

### 4.2 Hazard Types
| Hazard      | Effect on Drones        | Effect on Survivors  | Counter Module   |
|-------------|-------------------------|----------------------|------------------|
| Fire        | Battery drain ×2        | HP loss (fast)       | Fire Suppressor  |
| Radiation   | Slow malfunction        | HP loss (medium)     | Rad Scrubber     |
| Flooding    | Speed halved            | HP loss (slow)       | Pump Module      |
| Toxic Gas   | Sensor blackout         | HP loss (fast)       | Cargo (gas mask) |
| Power Down  | No lights, scan range=0 | Panic (see 5.2)      | Cargo (power cell)|

### 4.3 Passage Types

Connections between hex rooms have a type that restricts which drones can traverse:

| Passage      | Fits              | Notes                                |
|--------------|-------------------|--------------------------------------|
| Corridor     | All               | Standard hex-neighbor connection     |
| Blast Door   | All (if powered)  | Sealed until engineer restores power |
| Duct         | Mosquito, Wasp    | Smaller passage between neighbors    |
| Breach       | All               | Damages drones passing through       |

### 4.4 Vent Connections (long-range overlay)

In addition to hex-neighbor passages, **vent tunnels** can link any two rooms
regardless of distance:

- Vents are a direct graph edge between two hex rooms (no intermediate nodes)
- **Only Mosquito-class drones** can use vents (size restriction)
- Visually rendered as a thin pipe mesh running between the two rooms
- Drone enters one end, travels for a delay proportional to distance, exits the other
- Hazards (gas, fire) can also spread through vents
- Vents can be blocked and require clearing

From the RTS top-down view:
```
  ┌───┐         ┌───┐         ┌───┐
  │ A │════╗    │ B │─────────│ C │
  └───┘    ║    └───┘         └───┘
    │      ║                    │
  ┌───┐    ║    ┌───┐         ┌───┐
  │ D │────╬────│ E │─────────│ F │
  └───┘    ║    └───┘         └───┘
           ║                    │
           ╚═══════════════►  ┌───┐
               vent pipe      │ G │
              (A → G)         └───┘

  ═══  vent (Mosquito only)
  ───  corridor (all drones)
```

Tactical value: Mosquito scouts vent shortcuts to find survivors behind
blocked corridors, but the main fleet still needs to clear the hex path
for extraction.

### 4.5 Hazard Spread
Every N seconds, hazards spread to one adjacent connected room (unless a door is
sealed or the hazard is neutralized). This creates urgency — delay costs more rooms.

---

## 5. SURVIVOR SYSTEM

### 5.1 Survivor Stats
- **HP** — ticks down based on room hazard; at 0 the survivor is lost
- **Panic** — increases in dark/hazardous rooms; high panic = won't follow evac drone
- **Injury** — some survivors are immobile (must be carried via Survivor Pod)

### 5.2 Survivor Actions (AI-driven)
- Calm survivors can slowly walk toward a drone's waypoint (guided extraction)
- Panicked survivors may run into hazardous rooms
- Injured survivors cannot move without Survivor Pod

### 5.3 Supplies
Delivering supplies to a survivor's room stabilizes them:
- **O₂ Tank** → slows HP loss from gas/flooding
- **Med Kit** → heals HP over time
- **Calm Beacon** → reduces panic

---

## 6. BASE / COMMAND POST

The player's base is a special hex room (the entry point). Functions:
- **Drone Bay** — deploy/recall/recharge drones
- **Module Swap** — change drone loadouts (takes time)
- **Supply Stock** — limited supplies per mission; resupply via secondary objectives
- **Comms** — shows all known survivor locations and status

---

## 7. ECONOMY / PROGRESSION

### 7.1 Mission Resources
- **Credits** — earn per survivor rescued, bonus for speed / no losses
- **Salvage** — found in rooms, used to unlock modules
- **Intel** — scouting reveals the full map faster in future runs

### 7.2 Between Missions
- **Unlock** new chassis and modules
- **Upgrade** existing modules (Fire Suppressor Mk2 = clears fire faster)
- **Hire** more drone pilots (concurrent drone cap increases)
- **Research** passive bonuses (longer battery, faster recharge)

---

## 8. MISSION STRUCTURE

### 8.1 Generation
Each mission procedurally generates:
- Hex room layout (our HexMapGenerator with varied seeds/sizes)
- Hazard placement (scaling with difficulty)
- Survivor count and placement (deeper = more injured)
- Optional objectives (recover black box, seal reactor, etc.)

### 8.2 Difficulty Scaling
| Factor               | Easy    | Medium  | Hard    |
|----------------------|---------|---------|---------|
| Room count           | 12-16   | 18-24   | 28-36   |
| Survivors            | 3-4     | 5-8     | 8-12    |
| Hazard spread rate   | 45s     | 30s     | 18s     |
| Starting drones      | 4       | 3       | 2       |
| Global timer         | 10 min  | 7 min   | 5 min   |

### 8.3 Win / Lose
- **Win** — extract the required number of survivors before time runs out
- **Lose** — timer expires, or too many survivors lost
- **Score** — based on survivors saved, time remaining, drones intact, supplies used

---

## 9. UI LAYOUT (RTS aerial view)

```
┌─────────────────────────────────────────────────┐
│  [Timer]    [Survivors: 3/8]    [Credits]       │  ← Top bar
│                                                 │
│                                                 │
│              ┌───┐   ┌───┐                      │
│         ┌───┐│ ? │───│ ☢ │                      │
│         │ ✓ │└───┘   └───┘                      │  ← Hex map
│         └───┘  │       │                        │     (main view)
│           │  ┌───┐   ┌───┐                      │
│         ┌───┐│ 🔥│───│ 👤│                      │
│         │ ⬡ │└───┘   └───┘                      │
│         └───┘                                   │
│                                                 │
├───────────────────────────────┬─────────────────┤
│ [Drone1][Drone2][Drone3]      │ Selected Drone  │  ← Bottom bar
│  🔋80%  🔋45%   🔋100%       │ Hornet + Cargo  │    (drone cards
│                               │ Module: FireSup │     + details)
└───────────────────────────────┴─────────────────┘
```

---

## 10. IMPLEMENTATION PHASES

### Phase 1 — Core Prototype
- Hex map with fog of war
- One drone type (Hornet), manual movement (click-to-move)
- Rooms with simple hazards (fire)
- Survivors with HP countdown
- Basic extraction mechanic (bring drone to survivor → to base)
- Timer

### Phase 2 — Drone Customization
- Chassis + module system
- Multiple drone types
- Module effects (fire suppression, scanning, cargo)
- Drone battery + recharge at base

### Phase 3 — Full Hazard System
- All hazard types + spread mechanic
- Supplies (O₂, meds, calm beacon)
- Survivor panic + injury states
- Blocked/collapsed corridors

### Phase 4 — Meta Progression
- Mission select screen
- Credits / salvage economy
- Unlock / upgrade tree
- Difficulty scaling

### Phase 5 — Polish
- Sound design, particle FX
- Tutorial mission
- UI polish, minimap
- Balancing pass
