# Pigeon Fancier — Complete Game Guide

Reference document for autonomous fancier management.
Source: https://pigeonfancier.com/nl/wiki/

---

## 1. Pigeon Attributes

### Main Skills (6)

| Skill | Role |
|---|---|
| Speed | Raw flight velocity |
| Stamina | Endurance over distance |
| Aerodynamics | Reduces air resistance, critical in headwind |
| Flight Technique | Technical flight ability |
| Navigation | Orientation over long distances |
| Intelligence | Decision-making and route efficiency |

### Secondary Skills (2)

| Skill | Role |
|---|---|
| Libido | Breeding speed — high libido = significantly faster breeding |
| Night Flying | Keeps pigeon on course during fog, darkness, or low visibility |

### Experience

- Increases by participating in flights.
- Gain depends on number of flights and total flight distance.
- National flights yield the most experience, training flights the least.
- An experienced pigeon with slightly lower skills can beat a younger, stronger pigeon.

### Form

- Measured in 7 levels.
- Cannot be trained directly; changes through training and a hidden form trend.
- The form trend (rising or falling) shifts only a few times per season.
- Trends are hidden — observe weekly changes to infer direction.

### Age Lifecycle

- Born from a breeding pair.
- Breedable and raceable at 3 months (= 3 real game weeks, since 1 month = 1 game week).
- Peak performance during prime age.
- Decline starts around 3.5 years — skills begin to deteriorate, insurance premium drops.

### Insurance Premium

- Paid weekly per pigeon.
- Recalculated at end of each season:
  - **Increases** if skills improved.
  - **Unchanged** if skills stayed the same.
  - **Decreases** when pigeon declines due to age (~3.5+ years).
- **Death payout**: if a pigeon dies during a flight, the insurer pays 7.5x the premium.
- **Dump payout**: 1x the current insurance premium (one-time, instant removal).

---

## 2. Flight System

### Distance Categories

| Category | Distance |
|---|---|
| Short | 0–200 km |
| Middle | 200–500 km |
| Long | > 500 km |

### Flight Types

| Type | Cost | Skill Training | Experience | Risk |
|---|---|---|---|---|
| Training | EUR 100 flat (unlimited pigeons) | Best targeted | Least | Low |
| Regional | EUR 10 per pigeon | Good balance | Moderate | Moderate |
| National | EUR 10 per pigeon | Ultimate test | Most | Highest |

### Rules

- Maximum **2 flights per week** per pigeon — spread roster across flights.
- Must register at least **12 hours in advance**.
- Breeding pigeons **cannot** participate in flights.
- Sending a breeding pigeon to a flight **resets breeding progress entirely**.

### Energy

- Visible only when pigeon is at home (not flying).
- Shows starting energy for the next flight.
- **A pigeon that has not fully recovered will perform poorly.** Never enter a tired pigeon.

### Training Flights (Custom)

- Player chooses date/time and release point.
- Must be scheduled at least 1 day in advance.
- Cost: EUR 100 fixed (no per-pigeon fee; all pigeons fly free).
- Visibility: Public (any fancier can join free) or Private (only you, optionally invite others).

### Live Tracking

- Active flights show a rotating animation.
- Live page auto-refreshes every 2 minutes.

---

## 3. Training System

### Training Focus Options

| Focus | Target |
|---|---|
| Condition | Physical attributes (Speed, Stamina) |
| Strategic | Tactical attributes (Aerodynamics, Flight Technique, Navigation, Intelligence) |
| General | Balanced across all skills, but slower progression per skill |

### Training Matrix (Flight Distance × Focus)

| Distance | General | Condition | Strategic |
|---|---|---|---|
| **Short** (0–200 km) | Speed, Aerodynamics, Intelligence | Speed | Aerodynamics, Intelligence |
| **Middle** (200–500 km) | Speed, Stamina, Flight Technique | Speed, Stamina | Flight Technique |
| **Long** (> 500 km) | Stamina, Navigation, Intelligence | Stamina | Navigation, Intelligence |

### Training Reports

- Updated weekly on **Monday evening**.
- Shows which attributes increased or decreased by one level.
- Decreases are caused by age-related decline.

### Decision Logic for Autonomous Training

1. Assess each pigeon's weakest skills relative to its breed strengths.
2. Match training focus to the upcoming flight distances this week.
3. If a pigeon specializes in short flights (high Speed, low Stamina): use Condition focus + short flights.
4. If a pigeon specializes in long flights (high Navigation/Stamina): use Strategic focus + long flights.
5. For all-rounders or young pigeons: use General to develop evenly.
6. Rotate training focus across the season based on the flight calendar.

---

## 4. Breeding

### Requirements

- Exactly 1 cock + 1 hen.
- Minimum age: 3 months (3 game weeks).
- Must have available breeding loft section (3 space units each).

### Speed Factors

- Same breed pairs breed faster.
- Higher libido on both pigeons = significantly faster breeding.
- Longer pair duration = faster follow-up nests + higher chance of a better youngster.

### Critical Rules

- **Splitting a pair (even briefly) or sending a paired pigeon to fly resets ALL breeding progress.**
- **21-day incompatibility rule**: if no offspring after 21 days, the pair will NEVER produce together. Unpair and try a new combination immediately.
- **Feed distribution has NO effect on breeding pair performance or status.**

### Genetics

- Youngster qualities determined by both parents AND grandparents.
- If both parents excel in the same attributes, strong chance of the youngster inheriting or surpassing those strengths.
- **Inbreeding** is possible but risky: health may suffer, market value drops (pedigree is publicly visible), and both positive and negative trait variations can occur.

### Decision Logic for Autonomous Breeding

1. Pair pigeons with complementary strengths to produce well-rounded offspring.
2. Prioritize pigeons with high libido for breeding pairs.
3. Never interrupt a breeding pair — do not enroll paired pigeons in flights.
4. Monitor the 21-day timer: if day 21 arrives with no offspring, immediately unpair and rotate partners.
5. Check inbreeding coefficient: avoid pairing pigeons that share grandparents unless strategically justified.
6. Keep enough breeding loft sections reserved (3 units per pair).

---

## 5. Disease Management

### Disease Reference

| Disease | Cause | Severity | Notes |
|---|---|---|---|
| Cut wound | Flight injury | Low | Heals with a few days rest |
| Bone fracture | Severe flight injury | High | Weeks of recovery; without treatment may permanently ground pigeon |
| Paramyxo | Contagious virus | Very High | Months of sidelining; possible permanent neurological damage (twisted neck) |
| Pox | Infection | Medium | Wart-like growths on legs, head, beak |
| Paratyphoid | Contagious | High | Affects nerves/organs, causes diarrhea; pigeon must stay in loft |
| Coccidiosis | Intestinal parasite | Medium | Reduces appetite, green watery droppings |
| Hexamitiasis | Common (~80% carriers) | Very High | Yellow growths on beak/organs; often fatal (pigeon starves to death) |
| Canker | Common infection | Medium | Affects mucous membranes in throat and digestive organs |

### Prevention

- **Loft hygiene is critical** — clean loft sections reduce outbreak risk.
- Buy and apply crushed corn cob bedding from the store.

### Treatment

- Buy the correct medication from the store for the specific disease.
- **Administer treatment daily** for optimal recovery.
- Treated pigeons spread disease much less (reduced transmission).
- Untreated diseases become increasingly severe; the pigeon may die.

### Decision Logic for Autonomous Health Management

1. On every sync, check all pigeons for `disease` field.
2. If a pigeon is sick: identify the disease, check store inventory for correct medication.
3. If medication not in stock: buy it from the store immediately.
4. Administer treatment to the sick pigeon daily.
5. During severe outbreaks (multiple pigeons sick with contagious disease): consider dumping the most severely affected to protect the flock.
6. Never enter a sick pigeon in a flight.

---

## 6. Financial Management

### Income Sources

| Source | Details |
|---|---|
| Sponsors | Fixed weekly contribution; max 3 active at once. Good results = better deals; poor results = lower offers. After a contract expires, check daily for new offers. |
| Prize money | From Regional and National flights; updated Saturday evening. |
| Own contribution | Weekly investment from own funds. |
| Transfer sales | Selling pigeons on the auction market (minus 5% commission). |
| Dumping | 1x insurance premium per dumped pigeon (one-time). |

### Expenses

| Expense | Details |
|---|---|
| Insurance premiums | Weekly total for all pigeons. |
| Food | Must maintain stock; 30g/day per pigeon. |
| Entry fees | EUR 10/pigeon for regional/national; EUR 100 flat for training flights. |
| Medication | Variable; buy as needed from store. |
| Loft upgrades | One-time costs (EUR 3,000–30,000). |
| Bedding | Purchased from store for hygiene. |

### Debt Rules

- **Interest**: when capital drops below EUR 0, pay **5% weekly interest** on total debt.
  - Example: -EUR 1,000 balance = EUR 50 weekly interest, before other costs.
- **Bankruptcy**: triggered when capital falls below **-EUR 5,000**.
  - You get exactly **14 days** to bring balance back above -EUR 5,000.
  - Sell valuable pigeons on the transfer market to free up capital.

### Decision Logic for Autonomous Finance

1. Monitor `finances.balance` on every sync.
2. **Alert threshold**: if balance < EUR 500, reduce spending (skip training flights, defer upgrades).
3. **Danger threshold**: if balance < EUR 0, take aggressive action:
   - Stop all non-essential purchases.
   - Sell weakest pigeons on the transfer market.
   - Dump pigeons with high premiums and declining skills.
4. **Bankruptcy prevention**: if balance < -EUR 4,000, immediately dump or sell to avoid the -EUR 5,000 trigger.
5. Always maintain 3 active sponsors. Accept new sponsor offers as soon as they appear.
6. Track weekly income vs expenses trend to predict future balance.

---

## 7. Loft (Pen/Barn) Management

### Loft Tiers

| Tier | Capacity (units) | Cost |
|---|---|---|
| Run-Down Loft | 10 | Free (starting) |
| Loft | 25 | EUR 3,000 |
| Pigeon Villa | 50 | EUR 5,000 |
| Pigeon Complex | 100 | EUR 10,000 |
| Pigeon Paradise | 250 | EUR 30,000 |

### Space Allocation

- **Single loft section**: 1 space unit, houses 1 individual pigeon.
- **Breeding loft section**: 3 space units, houses 1 breeding pair (cock + hen).

### Hygiene

- **Crushed corn cob bedding** can be purchased from the store.
- Improves hygiene and comfort, reduces disease risk.
- Dirty loft = higher chance of disease outbreaks.

### Decision Logic

1. Monitor `pen.occupied` vs `pen.capacity` on every sync.
2. If occupied > 80% of capacity, plan an upgrade (if finances allow).
3. If the loft is dirty (`pen.dirt` field), trigger cleaning.
4. Buy bedding when stock is low.

---

## 8. Food Management

### Core Rules

- Each pigeon consumes exactly **30 grams of food per day** (auto-deducted from stock).
- Running out causes pigeons to weaken, perform poorly, and get sick quickly.
- During flights, pigeons draw extra energy from nutrition on top of starting energy.

### Ingredients (4 types)

| Ingredient | Type | Activation Time |
|---|---|---|
| Corn | Base | Medium |
| Barley | Base | Very short (fast-acting) |
| Grain | Base | Medium |
| Peanuts | Supplement | Very long (slow-acting) |

### Mix Rules

- Percentages of the 4 ingredients must add up to exactly **100%** (= 30g daily portion).
- No public "best recipe" — players keep winning ratios secret.
- **Balance between fast-acting (Barley) and slow-acting (Peanuts) ingredients is at least as important as individual ingredients.**

### Decision Logic for Autonomous Feeding

1. Monitor `food` stock levels on every sync (fields: `barley`, `grain`, `corn`, `peanut`).
2. Calculate days of food remaining: total_stock / (pigeon_count × 30g).
3. If less than 7 days of food remaining, buy more from the store.
4. Maintain a balanced mix — suggested starting ratio: 30% Corn, 25% Barley, 25% Grain, 20% Peanuts.
5. Adjust mix experimentally based on race results over time.
6. Never let food stock hit zero.

---

## 9. Transfer Market

### Selling (Creating a Listing)

- Go to team page, select pigeon(s), click "Sell".
- **Minimum starting price**: EUR 50.
- **Auction duration**: minimum 24 hours, maximum 72 hours.
- **Commission fee**: 5% deducted from final sale price.
  - Example: sell for EUR 1,000 → EUR 50 fee → EUR 950 net.
- **Listed pigeons cannot be entered in flights** until the auction ends.

### Buying (Bidding)

- Each bid must be at least **10% higher** than the current highest bid.
- **5-minute extension rule**: a bid in the final 5 minutes automatically extends the timer to 5 minutes.
- **Bid status**: Green = highest bidder; Red = outbid.
- Inactive button = insufficient balance for minimum next bid.

### Dumping

- Pigeon is permanently removed ("transferred to a local Chinese restaurant").
- Compensation: **1x current insurance premium** (one-time, instant).
- No more weekly premium owed after dumping.

### Decision Logic for Autonomous Transfers

**Buying:**
1. Use the existing price estimation engine to identify undervalued pigeons.
2. Only bid if: estimated value > current price × 1.3 (30% margin of safety).
3. Set max bid at estimated value × 0.9 (keep 10% buffer).
4. Never bid if it would push balance below EUR 500.

**Selling:**
1. Identify candidates: declining pigeons (age > 3.5 years, skills dropping), surplus pigeons when loft is full.
2. Set starting price at 80% of estimated value to attract bidders.
3. Use 48-hour auction duration (balance between exposure and speed).
4. Never sell a pigeon that is actively breeding or your top performer.

**Dumping:**
1. Dump when: pigeon has severe untreatable disease, pigeon is very old with very low skills, or during financial emergency.
2. Dump provides instant cash (1x premium) and eliminates ongoing premium costs.

---

## 10. Weather Strategy

### Weather Impacts

| Condition | Impact | Key Skill |
|---|---|---|
| Temperature | Different pigeons perform differently in warm vs cool weather | Breed-dependent |
| Tailwind | Increases flight speed | — |
| Headwind | Decreases flight speed | **Aerodynamics** reduces air resistance |
| Fog / Darkness | Reduced visibility, risk of getting lost | **Night Flying** keeps pigeon on course |

### 14-Day Forecast

- Available for strategic planning of which pigeons to enter in which flights.
- Check weather before every flight enrollment decision.

### Decision Logic for Weather-Based Flight Selection

1. Read the 14-day weather forecast from `/api/weather`.
2. For flights with strong headwind (high Beaufort): prioritize pigeons with high Aerodynamics.
3. For flights that may extend into darkness or fog: prioritize pigeons with high Night Flying.
4. For clear, calm conditions: prioritize pigeons with highest Speed.
5. Avoid entering fragile or recovering pigeons in bad weather conditions.

---

## 11. Season & Rankings

### Season Structure

- Each season lasts **12 weeks**.
- Major cash prizes at season end based on final standings.

### Class System

| Class | Districts | Fanciers per District |
|---|---|---|
| 1st (highest) | 1 | 16 |
| 2nd | 2 | 32 |
| 3rd | 4 | 64 |
| 4th | 8 | 128 |
| 5th (lowest) | 16 | 256 |

- New fanciers start in 5th class.
- **Promotion**: finish in **top 2** of your district.
- **Relegation**: finish in **bottom 6** of your district.

### Rankings

| Ranking | Scope | Based On |
|---|---|---|
| National Fanciers | All fanciers in the country | National Sunday flight points |
| National Pigeons | Best individual pigeons | National flight performance |
| Regional Fanciers | Your specific district/class | Regional flight points |
| Regional Pigeons | Best pigeons in your district | Regional flight performance |

### Raffle

- Draw every **Wednesday evening at 20:00**.
- Prizes: cash or items (including new pigeons).

---

## 12. Weekly Calendar

| Day | Event |
|---|---|
| Monday evening | Training reports updated |
| Wednesday 20:00 | Raffle draw |
| Saturday evening | Prize money / financial updates |
| Sunday | National flights |

---

## 13. Store

- Buy: food ingredients, disease medication, loft sections, bedding (crushed corn cobs).
- Emergency sell: sell items from stock for **50% of original purchase price**.

---

## 14. Autonomous Management Priority Order

When resources are limited, follow this priority:

1. **Health** — treat sick pigeons immediately (untreated = death risk).
2. **Food** — ensure stock never runs out (starving = weakness + sickness).
3. **Finance** — avoid bankruptcy at all costs (game over territory).
4. **Flights** — enter pigeons in flights to earn XP, prize money, and ranking points.
5. **Training** — optimize skill development for better race results.
6. **Breeding** — grow the flock with better genetics over time.
7. **Transfers** — buy/sell strategically to improve team quality.
8. **Loft** — upgrade only when capacity is genuinely needed.

---

## 15. Critical "Never Do" Rules

1. Never enter a pigeon that hasn't fully recovered its energy.
2. Never interrupt a breeding pair (no flights, no splitting even briefly).
3. Never let food stock reach zero.
4. Never let balance drop below -EUR 5,000.
5. Never enter more than 2 flights per week per pigeon.
6. Never enter a sick pigeon in a flight.
7. Never enter a breeding pigeon in a flight.
8. Never keep an incompatible pair together past 21 days.
9. Never buy from the transfer market if it would push balance below EUR 500.
10. Never sell your best-performing pigeon unless facing bankruptcy.
