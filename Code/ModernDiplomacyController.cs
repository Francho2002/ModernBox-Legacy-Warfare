using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Small, save-backed diplomacy layer.  WorldBox remains the authority for
    /// wars and alliances; this component only records modern commitments and
    /// applies their bounded economic/war consequences in staggered cycles.
    /// </summary>
    internal sealed class ModernDiplomacyController : MonoBehaviour
    {
        internal const string SaveKey = "modernbox.diplomacy.v1";
        private const int SaveVersion = 1;
        private const int MaxLedgerEntries = 18;
        private const int MaxGoldTransfer = 18;
        private const int MaxSanctionLoss = 3;
        private const int MaxJoinsPerWarCycle = 2;
        private const int MaximumActiveWarsFromUltimatums = 6;

        // MapBox persists between some loads; map_stats identifies the loaded map.
        private static MapBox cachedWorld;
        private static object cachedMapStats;
        private static readonly Dictionary<long, CountryState> states = new Dictionary<long, CountryState>();
        private static readonly Queue<DefenderJoin> defenderJoins = new Queue<DefenderJoin>();
        private static readonly HashSet<string> queuedJoinKeys = new HashSet<string>();
        private static bool defenderJoinsHydrated;
        private static int rosterCursor;
        private static int economyCursor;
        private static int crisisCursor;
        private static float nextRoster;
        private static float nextEconomy;
        private static float nextCrisis;
        private static float nextCleanup;
        private static float nextOrganization;

        [Serializable]
        private sealed class CountryState
        {
            public int version = SaveVersion;
            public List<DiplomacyLink> links = new List<DiplomacyLink>();
            public List<LedgerEntry> ledger = new List<LedgerEntry>();
            public float armsCredit;
            public long puppetMasterId;
            public long lastWarAt;
            public long lastUltimatumAt;
            public string organizationId;
            public long organizationResolutionAt;
            public List<DefenderJoin> pendingDefenderJoins = new List<DefenderJoin>();
        }

        [Serializable]
        private sealed class DiplomacyLink
        {
            public long otherId;
            public string type;
            public long createdAt;
            public long expiresAt;
            public int amount;
            public long issuerId;
            public bool accepted;
            public bool resolved;
        }

        [Serializable]
        private sealed class LedgerEntry
        {
            public long at;
            public string text;
        }

        [Serializable]
        private sealed class DefenderJoin
        {
            public long warAttacker;
            public long warDefender;
            public long joiner;
            public string reason;
        }

        private void Awake()
        {
            ResetForWorld();
            ModernBoxLogger.Log("[MX.Diplomacy] Sistema diplomático moderno activo.");
        }

        private void Update()
        {
            if (World.world == null)
                return;
            if (cachedWorld != World.world || cachedMapStats != World.world.map_stats)
                ResetForWorld();
            if (World.world.isPaused())
                return;

            float now = Time.time;
            try
            {
                if (now >= nextRoster)
                {
                    nextRoster = now + 20f;
                    EvaluateRoster();
                }
                if (now >= nextEconomy)
                {
                    nextEconomy = now + 42f;
                    RunEconomy();
                }
                if (now >= nextCrisis)
                {
                    nextCrisis = now + 60f;
                    RunCrises();
                }
                if (now >= nextCleanup)
                {
                    nextCleanup = now + 60f;
                    Cleanup();
                }
                if (now >= nextOrganization)
                {
                    nextOrganization = now + 150f;
                    RunOrganizations();
                }
            }
            catch (Exception ex)
            {
                // A malformed legacy relation must not turn into an exception
                // every frame. Every schedule is already advanced before work.
                ModernBoxLogger.Error("[MX.Diplomacy] Ciclo aislado: " + ex.Message);
            }
        }

        private static void ResetForWorld()
        {
            cachedWorld = World.world;
            cachedMapStats = World.world?.map_stats;
            states.Clear();
            defenderJoins.Clear();
            queuedJoinKeys.Clear();
            defenderJoinsHydrated = false;
            rosterCursor = 0;
            economyCursor = crisisCursor = 0;
            float now = Time.time;
            // Give WorldBox time to finish hydrating KingdomData before this
            // layer reads or writes its save-backed state.
            nextRoster = now + 8f;
            nextEconomy = now + 22f;
            nextCrisis = now + 30f;
            nextCleanup = now + 38f;
            nextOrganization = now + 50f;
        }

        internal static void ResetBeforeLoading()
        {
            cachedWorld = null;
            cachedMapStats = null;
            states.Clear();
            defenderJoins.Clear();
            queuedJoinKeys.Clear();
            defenderJoinsHydrated = false;
        }

        private static List<Kingdom> Civilizations()
        {
            if (World.world?.kingdoms == null)
                return new List<Kingdom>();
            return World.world.kingdoms.Where(k => k != null && k.isCiv()).OrderBy(k => k.id).ToList();
        }

        private static CountryState State(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;
            if (states.TryGetValue(kingdom.id, out CountryState current))
                return current;

            current = new CountryState();
            try
            {
                // BaseSystemData.get/set are backed by CustomDataContainer<string>
                // in build 719. Only an ID-only DTO is saved.
                string json = null;
                if (kingdom.data != null)
                    kingdom.data.get(SaveKey, out json, null);
                if (!string.IsNullOrEmpty(json))
                {
                    CountryState loaded = JsonConvert.DeserializeObject<CountryState>(json);
                    if (loaded != null && loaded.version == SaveVersion)
                        current = loaded;
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Warning("[MX.Diplomacy] Estado guardado ignorado para " + kingdom.id + ": " + ex.Message);
            }
            current.links = current.links ?? new List<DiplomacyLink>();
            current.ledger = current.ledger ?? new List<LedgerEntry>();
            current.pendingDefenderJoins = current.pendingDefenderJoins ?? new List<DefenderJoin>();
            states[kingdom.id] = current;
            return current;
        }

        private static void Save(Kingdom kingdom)
        {
            CountryState state = State(kingdom);
            if (kingdom?.data == null || state == null)
                return;
            try
            {
                kingdom.data.set(SaveKey, JsonConvert.SerializeObject(state));
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Warning("[MX.Diplomacy] No se pudo guardar el estado: " + ex.Message);
            }
        }

        // Treaty clocks are simulation years: saves do not age while closed.
        private static long Now() { return World.world?.map_stats == null ? 0L : World.world.map_stats.year; }
        private static long Years(int years) { return Math.Max(1, years); }
        private static string Key(long a, long b) { return a < b ? a + ":" + b : b + ":" + a; }

        private static Kingdom Find(long id)
        {
            return Civilizations().FirstOrDefault(k => k.id == id);
        }

        private static DiplomacyLink Link(Kingdom from, long to, string type)
        {
            return State(from)?.links.FirstOrDefault(l =>
                l.otherId == to && l.type == type && !l.resolved &&
                (l.expiresAt == 0 || l.expiresAt >= Now()));
        }

        private static bool HasLink(Kingdom a, Kingdom b, string type)
        {
            return a != null && b != null && (Link(a, b.id, type) != null || Link(b, a.id, type) != null);
        }

        private static bool HasActiveDirectedLink(Kingdom issuer, Kingdom target, string type)
        {
            return issuer != null && target != null && State(issuer).links.Any(l =>
                l.otherId == target.id && l.type == type && l.issuerId == issuer.id && !l.resolved &&
                (l.expiresAt == 0 || l.expiresAt >= Now()));
        }

        private static void AddSymmetricLink(Kingdom a, Kingdom b, string type, int amount, int years, bool accepted = true)
        {
            if (a == null || b == null || a == b || HasLink(a, b, type))
                return;
            long now = Now();
            State(a).links.Add(new DiplomacyLink { otherId = b.id, type = type, amount = amount, createdAt = now, expiresAt = now + Years(years), accepted = accepted });
            State(b).links.Add(new DiplomacyLink { otherId = a.id, type = type, amount = amount, createdAt = now, expiresAt = now + Years(years), accepted = accepted });
            Ledger(a, type + " con " + Name(b) + ".");
            Ledger(b, type + " con " + Name(a) + ".");
            Save(a); Save(b);
        }

        private static void AddDirectedLink(Kingdom issuer, Kingdom target, string type, int amount, int years, bool accepted = true)
        {
            if (issuer == null || target == null || issuer == target ||
                HasActiveDirectedLink(issuer, target, type))
                return;
            long now = Now();
            State(issuer).links.Add(new DiplomacyLink { otherId = target.id, type = type, amount = amount, issuerId = issuer.id, createdAt = now, expiresAt = now + Years(years), accepted = accepted });
            State(target).links.Add(new DiplomacyLink { otherId = issuer.id, type = type, amount = amount, issuerId = issuer.id, createdAt = now, expiresAt = now + Years(years), accepted = accepted });
            Ledger(issuer, type + " dirigido a " + Name(target) + ".");
            Ledger(target, type + " recibido de " + Name(issuer) + ".");
            Save(issuer); Save(target);
        }

        private static void CreateGuarantee(Kingdom guarantor, Kingdom protectedKingdom)
        {
            AddDirectedLink(guarantor, protectedKingdom, "garantía", 0, 16);
        }

        private static void Ledger(Kingdom kingdom, string text)
        {
            CountryState state = State(kingdom);
            if (state == null)
                return;
            state.ledger.Insert(0, new LedgerEntry { at = Now(), text = text });
            if (state.ledger.Count > MaxLedgerEntries)
                state.ledger.RemoveRange(MaxLedgerEntries, state.ledger.Count - MaxLedgerEntries);
        }

        private static string Name(Kingdom kingdom) { return string.IsNullOrEmpty(kingdom?.name) ? "Reino " + kingdom?.id : kingdom.name; }

        private static void EvaluateRoster()
        {
            List<Kingdom> roster = Civilizations();
            if (roster.Count < 2)
                return;
            for (int checkedPairs = 0; checkedPairs < 3; checkedPairs++)
            {
                Kingdom a = roster[rosterCursor % roster.Count];
                Kingdom b = roster[(rosterCursor / roster.Count + rosterCursor + 1) % roster.Count];
                rosterCursor++;
                if (a == b || a.isInWarWith(b) || !DiplomacyHelpers.areKingdomsClose(a, b))
                    continue;
                if (State(a).puppetMasterId == b.id || State(b).puppetMasterId == a.id)
                    continue;
                int score = Stable(a.id, b.id) % 10;
                if (score < 3 && a.isOpinionTowardsKingdomGood(b))
                    AddSymmetricLink(a, b, "pacto defensivo", 0, 18);
                else if (score < 5 && a.isOpinionTowardsKingdomGood(b))
                    AddSymmetricLink(a, b, "bloque comercial", 0, 14);
                else if (score == 5)
                {
                    Kingdom guarantor = a.power >= b.power ? a : b;
                    Kingdom protectedKingdom = guarantor == a ? b : a;
                    if (guarantor.power >= Math.Max(1, Mathf.RoundToInt(protectedKingdom.power * 1.25f)))
                        CreateGuarantee(guarantor, protectedKingdom);
                }
                else if (score == 6 && a.isOpinionTowardsKingdomGood(b))
                    TryBuyArms(
                        a.power >= b.power ? a : b,
                        a.power >= b.power ? b : a,
                        8);
                else if (score == 7 && !a.isOpinionTowardsKingdomGood(b))
                    CreateUltimatum(a.power >= b.power ? a : b, a.power >= b.power ? b : a);
                else if (score >= 8 && !a.isOpinionTowardsKingdomGood(b))
                    AddDirectedLink(a, b, "embargo", 0, 6);

                if (a.isOpinionTowardsKingdomGood(b) && TotalGold(a) > TotalGold(b) * 2 + 8)
                    TrySendEconomicAid(a, b, 6);
            }
        }

        private static int Stable(long a, long b)
        {
            unchecked { return (int)((a * 486187739L + b * 16777619L) & 0x7fffffff); }
        }

        private static void RunEconomy()
        {
            List<Kingdom> roster = Civilizations();
            for (int checkedKingdoms = 0; checkedKingdoms < Math.Min(3, roster.Count); checkedKingdoms++)
            {
                Kingdom kingdom = roster[economyCursor++ % roster.Count];
                CountryState state = State(kingdom);
                List<DiplomacyLink> activeLinks = state.links
                    .Where(l => !l.resolved && (l.expiresAt == 0 || l.expiresAt >= Now()))
                    .ToList();
                foreach (DiplomacyLink link in activeLinks)
                {
                    Kingdom other = Find(link.otherId);
                    if (other == null) continue;
                    if (link.type == "bloque comercial" && kingdom.id < other.id && !IsEmbargoed(kingdom, other))
                    {
                        // One capped, real transfer per pair. It is deliberately
                        // not a free resource generator and embargoes stop it.
                        Kingdom donor = TotalGold(kingdom) >= TotalGold(other) ? kingdom : other;
                        Kingdom receiver = donor == kingdom ? other : kingdom;
                        TransferGold(donor, receiver,
                            Math.Min(4, Math.Max(1, TotalGold(donor) / 30)),
                            "Beneficio de bloque comercial", false);
                    }
                }

                // Several countries may sanction the same target, but their
                // combined effect remains capped. This prevents coalitions from
                // erasing a small kingdom's treasury in one diplomacy cycle.
                int incomingSanctions = activeLinks.Count(l =>
                    l.type == "sanción" && l.issuerId != kingdom.id);
                if (incomingSanctions > 0)
                    TakeGold(kingdom, Math.Min(MaxSanctionLoss, incomingSanctions));

                ApplyPuppetTribute(kingdom);
            }
        }

        private static int TotalGold(Kingdom kingdom)
        {
            if (kingdom?.cities == null) return 0;
            int total = 0;
            foreach (City city in kingdom.cities)
                if (city != null) total += Math.Max(0, city.amount_gold);
            return total;
        }

        private static int TakeGold(Kingdom kingdom, int desired)
        {
            int remaining = Math.Max(0, desired);
            if (kingdom?.cities == null) return 0;
            foreach (City city in kingdom.cities)
            {
                if (remaining <= 0) break;
                int amount = Math.Min(remaining, Math.Max(0, city.amount_gold));
                if (amount > 0) { city.takeResource("gold", amount); remaining -= amount; }
            }
            return desired - remaining;
        }

        private static int GiveGold(Kingdom kingdom, int amount)
        {
            City city = kingdom?.cities?.FirstOrDefault(c => c != null && c.isAlive());
            if (city != null && amount > 0)
                return city.addResourcesToRandomStockpile("gold", amount);
            return 0;
        }

        private static bool TransferGold(
            Kingdom donor,
            Kingdom receiver,
            int requested,
            string reason,
            bool record = true)
        {
            if (donor == null || receiver == null || requested <= 0 || IsEmbargoed(donor, receiver)) return false;
            int offered = Math.Min(Math.Min(MaxGoldTransfer, requested), TotalGold(donor));
            if (offered <= 0) return false;
            // Stockpiles may decline a part of the offer. Debit only their
            // reported acceptance, after the recipient has accepted it.
            int accepted = GiveGold(receiver, offered);
            int paid = TakeGold(donor, Math.Min(accepted, offered));
            if (paid <= 0) return false;
            if (record)
            {
                Ledger(donor, reason + ": " + paid + " oro a " + Name(receiver) + ".");
                Ledger(receiver, reason + ": recibió " + paid + " oro de " + Name(donor) + ".");
                Save(donor);
                Save(receiver);
            }
            return true;
        }

        private static bool IsEmbargoed(Kingdom a, Kingdom b)
        {
            return HasLink(a, b, "embargo") || HasLink(a, b, "sanción");
        }

        private static void ApplyPuppetTribute(Kingdom puppet)
        {
            CountryState state = State(puppet);
            if (state.puppetMasterId == 0) return;
            Kingdom master = Find(state.puppetMasterId);
            if (master == null || !master.isCiv() ||
                puppet.power >= Math.Max(1, Mathf.RoundToInt(master.power * 0.90f)) ||
                !HasActiveDirectedLink(master, puppet, "estado títere"))
            {
                state.puppetMasterId = 0;
                Ledger(puppet, "Recuperó su independencia: terminó la relación de dependencia.");
                Save(puppet);
                return;
            }
            TransferGold(puppet, master,
                Math.Min(3, Math.Max(1, TotalGold(puppet) / 25)),
                "Tributo de estado títere", false);
        }

        private static void RunCrises()
        {
            HydrateDefenderJoins();
            int joins = 0;
            int processed = 0;
            while (defenderJoins.Count > 0 && joins < MaxJoinsPerWarCycle && processed < 8)
            {
                processed++;
                DefenderJoin request = defenderJoins.Dequeue();
                string requestKey = Key(request.warAttacker, request.warDefender) + ":" + request.joiner;
                queuedJoinKeys.Remove(requestKey);
                Kingdom attacker = Find(request.warAttacker), defender = Find(request.warDefender), joiner = Find(request.joiner);
                War war = attacker == null || defender == null ? null : World.world.wars.getWar(attacker, defender, false);
                bool completed = war == null || joiner == null ||
                    war.hasKingdom(joiner) || joiner.isEnemy(defender);
                if (!completed)
                {
                    try
                    {
                        war.joinDefenders(joiner);
                        Ledger(joiner, "Entró como defensor por " + request.reason + ".");
                        ModernBoxLogger.Log("[MX.Diplomacy] Adhesión defensiva: " +
                            joiner.id + " por " + request.reason + ".");
                        joins++;
                        completed = true;
                    }
                    catch (Exception ex)
                    {
                        // Keep the save-backed obligation and retry it in the
                        // next staggered crisis cycle instead of losing it.
                        if (queuedJoinKeys.Add(requestKey))
                            defenderJoins.Enqueue(request);
                        ModernBoxLogger.Warning("[MX.Diplomacy] Adhesión aplazada: " + ex.Message);
                        break;
                    }
                }
                if (completed && joiner != null)
                {
                    CountryState joinerState = State(joiner);
                    joinerState.pendingDefenderJoins.RemoveAll(p =>
                        p.warAttacker == request.warAttacker &&
                        p.warDefender == request.warDefender &&
                        p.joiner == request.joiner);
                    Save(joiner);
                }
            }

            // A proxy sponsor only transfers material to an already existing
            // participant's war. It is never joined to either side.
            List<Kingdom> roster = Civilizations();
            for (int checkedKingdoms = 0; checkedKingdoms < Math.Min(3, roster.Count); checkedKingdoms++)
            {
                Kingdom beneficiary = roster[crisisCursor++ % roster.Count];
                Kingdom enemy = null;
                using (var enemies = beneficiary.getEnemiesKingdoms())
                {
                    enemy = enemies?.FirstOrDefault(e =>
                        e != null && beneficiary.isInWarWith(e));
                }
                Kingdom sponsor = enemy == null ? null : roster.FirstOrDefault(k =>
                    k != beneficiary && k != enemy && !k.isInWarWith(enemy) &&
                    k.isOpinionTowardsKingdomGood(beneficiary) &&
                    DiplomacyHelpers.areKingdomsClose(k, beneficiary));
                if (sponsor != null && TrySupportProxyWar(sponsor, beneficiary, enemy))
                    break;
            }

            // At most one normal war per crisis cycle, after an expired ultimatum.
            for (int checkedKingdoms = 0; checkedKingdoms < Math.Min(3, roster.Count); checkedKingdoms++)
            {
                Kingdom source = roster[crisisCursor++ % roster.Count];
                if (Now() - State(source).lastWarAt < Years(8))
                    continue;
                DiplomacyLink ultimatum = State(source).links.FirstOrDefault(l => l.type == "ultimátum" && l.issuerId == source.id && !l.resolved && l.expiresAt <= Now());
                Kingdom target = ultimatum == null ? null : Find(ultimatum.otherId);
                if (target == null) continue;
                ultimatum.resolved = true;
                DiplomacyLink mirror = State(target).links.FirstOrDefault(l => l.type == "ultimátum" && l.issuerId == source.id && l.otherId == source.id && !l.resolved);
                if (mirror != null) mirror.resolved = true;
                bool advantage = source.power > target.power * 1.20f || source.countTotalWarriors() > target.countTotalWarriors() * 13 / 10;
                if (!ultimatum.accepted && advantage && !source.isInWarWith(target))
                {
                    if (World.world.wars.countActiveWars() < MaximumActiveWarsFromUltimatums &&
                        WarTypeLibrary.normal != null)
                    {
                        War created = World.world.wars.newWar(
                            source, target, WarTypeLibrary.normal);
                        if (created != null)
                        {
                            State(source).lastWarAt = Now();
                            Ledger(source, "Ultimátum rechazado; comenzó una guerra normal contra " + Name(target) + ".");
                            Ledger(target, "Rechazó un ultimátum de " + Name(source) + ".");
                        }
                    }
                    else
                    {
                        AddDirectedLink(source, target, "sanción", 0, 5);
                        Ledger(source, "Ultimátum rechazado; la escalada quedó limitada a sanciones.");
                    }
                }
                else if (ultimatum.accepted && !source.isInWarWith(target))
                {
                    // A submission creates a reversible protectorate, never an
                    // annexation: the target keeps its ruler, culture and cities.
                    State(target).puppetMasterId = source.id;
                    AddDirectedLink(source, target, "estado títere", 0, 30);
                    CreateGuarantee(source, target);
                    Ledger(source, Name(target) + " aceptó un estado títere con tributo limitado.");
                    Ledger(target, "Aceptó estado títere; conserva sus ciudades, rey y cultura.");
                }
                else Ledger(source, "Ultimátum cerrado sin guerra.");
                Save(source); if (target != null) Save(target);
                break;
            }
        }

        private static void Cleanup()
        {
            long now = Now();
            HashSet<long> alive = new HashSet<long>(Civilizations().Select(k => k.id));
            foreach (Kingdom kingdom in Civilizations())
            {
                CountryState state = State(kingdom);
                state.links.RemoveAll(l => !alive.Contains(l.otherId) || (l.expiresAt > 0 && l.expiresAt < now));
                // A link must exist on both sides to survive a save/load or kingdom death.
                state.links.RemoveAll(l => { Kingdom other = Find(l.otherId); return other == null || Link(other, kingdom.id, l.type) == null; });
                Save(kingdom);
            }
        }

        private static void CreateUltimatum(Kingdom source, Kingdom target)
        {
            if (source == null || target == null || Link(source, target.id, "ultimátum") != null || source.isInWarWith(target))
                return;
            CountryState sourceState = State(source);
            if (sourceState.puppetMasterId != 0 || State(target).puppetMasterId != 0)
                return;
            if (Now() - sourceState.lastUltimatumAt < Years(8)) return;
            bool accepts = target.power < source.power * 0.72f && target.countTotalWarriors() < source.countTotalWarriors();
            long deadline = Now() + Years(3);
            sourceState.links.Add(new DiplomacyLink { otherId = target.id, type = "ultimátum", issuerId = source.id, createdAt = Now(), expiresAt = deadline, accepted = accepts });
            State(target).links.Add(new DiplomacyLink { otherId = source.id, type = "ultimátum", issuerId = source.id, createdAt = Now(), expiresAt = deadline, accepted = accepts });
            sourceState.lastUltimatumAt = Now();
            Ledger(source, "Entregó un ultimátum a " + Name(target) + (accepts ? "; aceptación probable." : "; rechazo probable."));
            Ledger(target, "Recibió un ultimátum de " + Name(source) + (accepts ? "; aceptó condiciones." : "; rechazó condiciones."));
            Save(source); Save(target);
        }

        private static void RunOrganizations()
        {
            List<Kingdom> members = Civilizations().Where(k => State(k).organizationId == "liga-moderna").ToList();
            if (members.Count < 3)
            {
                List<Kingdom> roster = Civilizations();
                List<Kingdom> founders = new List<Kingdom>();
                foreach (Kingdom seed in roster)
                {
                    founders = roster.Where(candidate =>
                            candidate == seed ||
                            (!candidate.isEnemy(seed) && !candidate.isInWarWith(seed) &&
                             candidate.isOpinionTowardsKingdomGood(seed) &&
                             seed.isOpinionTowardsKingdomGood(candidate)))
                        .Take(6)
                        .ToList();
                    if (founders.Count >= 3)
                        break;
                }
                if (founders.Count < 3)
                    return;

                foreach (Kingdom founder in founders)
                {
                    State(founder).organizationId = "liga-moderna";
                    Ledger(founder, "Fundó la Liga Moderna (organización, no alianza militar).");
                    Save(founder);
                }
                return;
            }

            // Every member casts one deterministic vote. A resolution needs at
            // least 60%; the subject rotates with simulation time instead of
            // repeating the same policy forever.
            int vote = Stable(members[0].id, Now() / 5L + members.Count) % 3;
            int approvals = members.Count(member =>
                Stable(member.id, Now() / 3L + vote) % 100 < 72);
            if (approvals * 100 < members.Count * 60)
            {
                foreach (Kingdom member in members)
                {
                    Ledger(member, "La Liga no alcanzó el 60% para aprobar una resolución.");
                    Save(member);
                }
                return;
            }

            Kingdom lead = members[0];
            Kingdom external = Civilizations()
                .Where(k => !members.Contains(k))
                .OrderByDescending(k => members.Count(m =>
                    m.isEnemy(k) || !m.isOpinionTowardsKingdomGood(k)))
                .FirstOrDefault();
            if (vote == 0 && external != null)
            {
                foreach (Kingdom member in members.Take(3))
                    AddDirectedLink(member, external, "sanción", 0, 4);
                Ledger(lead, "Resolución de la Liga: sanción colectiva con pérdida total limitada.");
            }
            else if (vote == 1)
            {
                Kingdom donor = members.OrderByDescending(TotalGold).FirstOrDefault();
                Kingdom receiver = members.OrderBy(TotalGold).FirstOrDefault();
                if (donor != null && receiver != null && donor != receiver)
                    TransferGold(donor, receiver, 4, "Fondo común de la Liga");
                foreach (Kingdom member in members.Take(3)) Ledger(member, "Resolución de la Liga: fondo común transferido al miembro con menos oro.");
            }
            else
            {
                War mediated = World.world.wars.getActiveWars()
                    .FirstOrDefault(war =>
                        war != null && !war.hasEnded() && war.getDuration() >= 10 &&
                        members.Contains(war.getMainAttacker()) &&
                        members.Contains(war.getMainDefender()));
                if (mediated != null)
                {
                    Kingdom attacker = mediated.getMainAttacker();
                    Kingdom defender = mediated.getMainDefender();
                    World.world.wars.endWar(mediated, WarWinner.Peace);
                    Ledger(attacker, "La Liga medió una paz tras una guerra prolongada.");
                    Ledger(defender, "La Liga medió una paz tras una guerra prolongada.");
                    Save(attacker);
                    Save(defender);
                }
                foreach (Kingdom member in members.Take(3))
                    Ledger(member, mediated == null
                        ? "Resolución de la Liga: mediación sin guerra elegible."
                        : "Resolución de la Liga: mediación aprobada por al menos 60%.");
            }
            foreach (Kingdom member in members) { State(member).organizationResolutionAt = Now(); Save(member); }
        }

        private static void HydrateDefenderJoins()
        {
            if (defenderJoinsHydrated)
                return;
            foreach (Kingdom kingdom in Civilizations())
            {
                CountryState state = State(kingdom);
                foreach (DefenderJoin request in state.pendingDefenderJoins.ToList())
                {
                    if (request == null || request.joiner != kingdom.id)
                        continue;
                    string key = Key(request.warAttacker, request.warDefender) + ":" + request.joiner;
                    if (queuedJoinKeys.Add(key))
                        defenderJoins.Enqueue(request);
                }
            }
            defenderJoinsHydrated = true;
        }

        internal static void NoticeWarCreated(Kingdom attacker, Kingdom defender)
        {
            if (attacker == null || defender == null || attacker == defender) return;
            foreach (Kingdom candidate in Civilizations())
            {
                if (candidate == attacker || candidate == defender || candidate.isInWarWith(defender)) continue;
                bool pact = HasLink(candidate, defender, "pacto defensivo");
                bool guarantee = State(candidate).links.Any(l =>
                    l.otherId == defender.id && l.type == "garantía" &&
                    l.issuerId == candidate.id && !l.resolved &&
                    (l.expiresAt == 0 || l.expiresAt >= Now()));
                if (!pact && !guarantee) continue;
                string key = Key(attacker.id, defender.id) + ":" + candidate.id;
                if (queuedJoinKeys.Add(key))
                {
                    DefenderJoin request = new DefenderJoin
                    {
                        warAttacker = attacker.id, warDefender = defender.id, joiner = candidate.id,
                        reason = guarantee ? "garantía de independencia" : "pacto defensivo"
                    };
                    defenderJoins.Enqueue(request);
                    State(candidate).pendingDefenderJoins.Add(request);
                    Save(candidate);
                }
            }
        }

        internal static bool TryBuyArms(Kingdom seller, Kingdom buyer, int price)
        {
            if (HasActiveDirectedLink(seller, buyer, "venta de armas")) return false;
            if (!TransferGold(buyer, seller, price, "Venta de armas")) return false;
            CountryState state = State(buyer);
            state.armsCredit = Mathf.Clamp(state.armsCredit + 0.25f, 0f, 0.30f);
            AddDirectedLink(seller, buyer, "venta de armas", price, 6);
            Ledger(buyer, "Crédito de adquisición del " + Mathf.RoundToInt(state.armsCredit * 100f) + "% para la próxima producción.");
            Save(buyer);
            return true;
        }

        internal static bool TrySendEconomicAid(Kingdom donor, Kingdom receiver, int amount)
        {
            if (HasActiveDirectedLink(donor, receiver, "ayuda económica")) return false;
            bool done = TransferGold(donor, receiver, amount, "Ayuda económica");
            if (done) AddDirectedLink(donor, receiver, "ayuda económica", amount, 5);
            return done;
        }

        internal static bool TrySupportProxyWar(Kingdom sponsor, Kingdom beneficiary, Kingdom enemy)
        {
            if (sponsor == null || beneficiary == null || enemy == null || sponsor.isInWarWith(enemy) || !beneficiary.isInWarWith(enemy)) return false;
            if (HasActiveDirectedLink(sponsor, beneficiary, "apoyo proxy")) return false;
            if (!TrySendEconomicAid(sponsor, beneficiary, 8)) return false;
            AddDirectedLink(sponsor, beneficiary, "apoyo proxy", 8, 4);
            // Discovery harms the covert sponsor; the enemy imposes the sanction.
            if (Stable(sponsor.id, enemy.id) % 5 == 0) AddDirectedLink(enemy, sponsor, "sanción", 0, 3);
            return true;
        }

        internal static void ApplyArmsCredit(City city)
        {
            Kingdom kingdom = city?.kingdom;
            CountryState state = State(kingdom);
            if (city == null || state == null || state.armsCredit < 0.20f) return;
            // Production has already paid its normal construction cost. Refund a
            // bounded real gold subsidy once, so the credit is a 20-30% saving
            // without bypassing caps, resource checks, or the spawn path.
            int refund = Mathf.Clamp(Mathf.RoundToInt(8f * state.armsCredit), 1, 2);
            int accepted = city.addResourcesToRandomStockpile("gold", refund);
            if (accepted <= 0)
                return;
            Ledger(kingdom, "Crédito de armas aplicado: reembolso de " + accepted + " oro en producción.");
            state.armsCredit = 0f;
            Save(kingdom);
        }

        internal static int PageCount { get { return Math.Max(1, Civilizations().Count); } }
        internal static string BuildReport(int page)
        {
            List<Kingdom> kingdoms = Civilizations();
            if (kingdoms.Count == 0) return "<color=#FFCC77>No hay civilizaciones activas.</color>";
            Kingdom kingdom = kingdoms[Mathf.Clamp(page, 0, kingdoms.Count - 1)];
            CountryState state = State(kingdom);
            StringBuilder text = new StringBuilder();
            text.AppendLine("<color=#F5D66D><b>Diplomacia moderna: " + Escape(Name(kingdom)) + "</b></color>");
            text.AppendLine("Oro: " + TotalGold(kingdom) + " | Crédito de armas: " + Mathf.RoundToInt(state.armsCredit * 100f) + "%");
            text.AppendLine("Títere de: " + (state.puppetMasterId == 0 ? "ninguno" : Escape(Name(Find(state.puppetMasterId)))));
            List<Kingdom> subjects = Civilizations()
                .Where(candidate => State(candidate).puppetMasterId == kingdom.id)
                .Take(4)
                .ToList();
            text.AppendLine("Estados sujetos: " + (subjects.Count == 0
                ? "ninguno"
                : string.Join(", ", subjects.Select(subject => Escape(Name(subject))).ToArray())));
            text.AppendLine("Organización: " + (string.IsNullOrEmpty(state.organizationId)
                ? "ninguna"
                : "Liga Moderna (1 voto por miembro; mayoría del 60%)"));
            text.AppendLine("<color=#AFC7D6>Sistemas: pactos · garantías · comercio · embargos · sanciones · ultimátums · títeres · armas · ayuda · proxy · organizaciones</color>");
            text.AppendLine("<color=#AFC7D6>Relaciones y efectos</color>");
            List<DiplomacyLink> visibleLinks = state.links
                .Where(l => !l.resolved && (l.expiresAt == 0 || l.expiresAt >= Now()))
                .Take(8)
                .ToList();
            foreach (DiplomacyLink link in visibleLinks)
                text.AppendLine("• " + Direction(kingdom, link) + Escape(link.type) +
                    " — " + Escape(Name(Find(link.otherId))) + Effect(link.type));
            if (visibleLinks.Count == 0) text.AppendLine("• Sin compromisos modernos activos.");
            text.AppendLine("<color=#AFC7D6>Crisis / registro</color>");
            foreach (LedgerEntry entry in state.ledger.Take(6)) text.AppendLine("• " + Escape(entry.text));
            if (state.ledger.Count == 0) text.AppendLine("• Sin eventos recientes.");
            return text.ToString();
        }

        private static string Effect(string type)
        {
            switch (type)
            {
                case "pacto defensivo": return " (se une a la defensa, cola limitada)";
                case "garantía": return " (garantía de independencia; defensa automática)";
                case "bloque comercial": return " (redistribución comercial pequeña)";
                case "embargo": return " (bloquea comercio, ayuda y armas)";
                case "sanción": return " (pérdida combinada máxima de 3 oro por ciclo)";
                case "ultimátum": return " (plazo de 3 años; sumisión o escalada limitada)";
                case "estado títere": return " (tributo; conserva rey, cultura, ciudades y edificios)";
                case "venta de armas": return " (crédito del 20-30% para la próxima producción)";
                case "ayuda económica": return " (transferencia real entre tesorerías)";
                case "apoyo proxy": return " (ayuda sin entrar en la guerra)";
                default: return string.Empty;
            }
        }

        private static string Direction(Kingdom owner, DiplomacyLink link)
        {
            if (link.issuerId == 0)
                return string.Empty;
            return link.issuerId == owner.id ? "emitido: " : "recibido: ";
        }

        private static string Escape(string value) { return (value ?? "desconocido").Replace("<", "&lt;").Replace(">", "&gt;"); }
    }

    [HarmonyPatch]
    internal static class ModernDiplomacyWarPatch
    {
        // Signature-independent only at patch-registration time; the postfix
        // merely observes creation and deliberately queues, never joins a war.
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(WarManager), "newWar");
        }

        private static void Postfix(object[] __args)
        {
            if (__args == null || __args.Length < 2) return;
            Kingdom attacker = __args[0] as Kingdom;
            Kingdom defender = __args[1] as Kingdom;
            ModernDiplomacyController.NoticeWarCreated(attacker, defender);
        }
    }

    [HarmonyPatch(typeof(SaveManager), "prepareLoading")]
    internal static class ModernDiplomacyLoadResetPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            ModernDiplomacyController.ResetBeforeLoading();
        }
    }
}
