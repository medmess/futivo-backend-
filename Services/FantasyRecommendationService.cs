using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public sealed class FantasyRecommendationService
{
  private static readonly IReadOnlyDictionary<string, int> SquadQuotas =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
      {
        ["GK"] = 2,
        ["DEF"] = 5,
        ["MID"] = 5,
        ["FWD"] = 4
      };

  private static readonly IReadOnlyList<VariantDefinition> Variants =
  [
      new(
            "balanced",
            "Equipe recommandee equilibree",
            "Budget, forme et regularite avec un risque controle.",
            new ScoreWeights(0.30, 0.20, 0.20, 0.15, 0.10, 0.05)),
        new(
            "lowRisk",
            "Equipe a faible risque",
            "Priorite aux titulaires reguliers et aux profils constants.",
            new ScoreWeights(0.22, 0.30, 0.14, 0.10, 0.20, 0.04)),
        new(
            "offensive",
            "Equipe offensive",
            "Plus de poids aux joueurs creatifs et aux attaquants en forme.",
            new ScoreWeights(0.38, 0.16, 0.16, 0.18, 0.08, 0.04)),
        new(
            "value",
            "Meilleur rapport qualite/prix",
            "Selection optimisee pour economiser le budget sans perdre trop de potentiel.",
            new ScoreWeights(0.24, 0.16, 0.38, 0.10, 0.08, 0.04)),
        new(
            "differential",
            "Choix differentiel",
            "Inclut plus de joueurs moins populaires pour differencier ton equipe.",
            new ScoreWeights(0.24, 0.16, 0.16, 0.14, 0.08, 0.22))
  ];

  public RecommendedSquadResponse RecommendSquads(FantasyRecommendationRequest request)
  {
    var eligiblePlayers = request.Players
        .Where(player => !player.IsInjured && !player.IsSuspended)
        .Where(player => SquadQuotas.ContainsKey(player.Position))
        .Where(player => player.Price > 0 && player.Price <= request.Budget)
        .ToList();

    var variants = Variants
        .Select(variant => BuildVariant(
            variant,
            eligiblePlayers,
            request.Budget,
            Math.Max(1, request.MaxPlayersPerClub),
            request.UserSeed ?? string.Empty))
        .OrderByDescending(variant => variant.IsComplete)
        .ThenByDescending(variant => variant.Score)
        .ToList();

    return new RecommendedSquadResponse(
        variants,
        "Rule-based recommendations only. They are not a guaranteed winning team.",
        DateTimeOffset.UtcNow);
  }

  public RecommendedPlayerResponse RecommendPlayers(FantasyPlayerRecommendationRequest request)
  {
    var excluded = (request.ExcludedPlayerIds ?? Array.Empty<string>())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var maxPoints = Math.Max(1, request.Players.Max(player => player.Points));
    var candidates = request.Players
        .Where(player => !player.IsInjured && !player.IsSuspended)
        .Where(player => player.Position.Equals(request.Position, StringComparison.OrdinalIgnoreCase))
        .Where(player => player.Price <= request.RemainingBudget)
        .Where(player => !excluded.Contains(player.Id))
        .Select(player =>
        {
          var score = ScorePlayer(
                  player,
                  Variants[0].Weights,
                  maxPoints,
                  request.UserSeed ?? string.Empty,
                  Variants[0].Key);
          return new RecommendedPlayerDto(
                  player,
                  Math.Round(score * 100, 1),
                  BuildReasons(player, maxPoints, request.RemainingBudget));
        })
        .OrderByDescending(player => player.Score)
        .ThenBy(player => player.Player.Price)
        .Take(8)
        .ToList();

    return new RecommendedPlayerResponse(candidates, DateTimeOffset.UtcNow);
  }

  private static RecommendedSquadVariant BuildVariant(
      VariantDefinition variant,
      IReadOnlyList<FantasyRecommendationPlayer> players,
      decimal budget,
      int maxPlayersPerClub,
      string userSeed)
  {
    var maxPoints = Math.Max(1, players.Count == 0 ? 1 : players.Max(player => player.Points));
    var picked = new List<FantasyRecommendationPlayer>();
    var clubCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var remainingBudget = budget;

    foreach (var (position, quota) in SquadQuotas)
    {
      for (var slot = 0; slot < quota; slot++)
      {
        var candidate = players
            .Where(player => player.Position.Equals(position, StringComparison.OrdinalIgnoreCase))
            .Where(player => picked.All(selected => selected.Id != player.Id))
            .Where(player => player.Price <= remainingBudget)
            .Where(player => !clubCounts.TryGetValue(player.Club, out var count) || count < maxPlayersPerClub)
            .OrderByDescending(player => ScorePlayer(player, variant.Weights, maxPoints, userSeed, variant.Key))
            .ThenBy(player => player.Price)
            .FirstOrDefault();

        if (candidate is null)
        {
          candidate = players
              .Where(player => player.Position.Equals(position, StringComparison.OrdinalIgnoreCase))
              .Where(player => picked.All(selected => selected.Id != player.Id))
              .Where(player => player.Price <= remainingBudget)
              .OrderByDescending(player => ScorePlayer(player, variant.Weights, maxPoints, userSeed, variant.Key) - 0.08)
              .ThenBy(player => player.Price)
              .FirstOrDefault();
        }

        if (candidate is null)
        {
          continue;
        }

        picked.Add(candidate);
        remainingBudget -= candidate.Price;
        clubCounts[candidate.Club] = clubCounts.GetValueOrDefault(candidate.Club) + 1;
      }
    }

    var totalScore = picked.Count == 0
        ? 0
        : picked.Average(player => ScorePlayer(player, variant.Weights, maxPoints, userSeed, variant.Key)) * 100;
    var usedBudget = picked.Sum(player => player.Price);

    return new RecommendedSquadVariant(
        variant.Key,
        variant.Title,
        variant.Subtitle,
        picked
            .OrderBy(player => PositionOrder(player.Position))
            .ThenByDescending(player => ScorePlayer(player, variant.Weights, maxPoints, userSeed, variant.Key))
            .ToList(),
        usedBudget,
        Math.Round(totalScore, 1),
        picked.Count == SquadQuotas.Values.Sum(),
        BuildSquadReasons(variant, picked, maxPoints, budget));
  }

  private static double ScorePlayer(
      FantasyRecommendationPlayer player,
      ScoreWeights weights,
      int maxPoints,
      string userSeed,
      string variantKey)
  {
    var recent = Clamp(player.RecentPerformance ?? (double)player.Points / maxPoints);
    var minutes = Clamp(player.ExpectedPlayingTime ?? (0.68 + recent * 0.22));
    var value = Clamp(((double)player.Points + 4) / Math.Max(4, (double)player.Price) / 3.2);
    var fixture = Clamp(1 - (player.FixtureDifficulty ?? 0.5));
    var consistency = Clamp(player.Consistency ?? (0.55 + recent * 0.35));
    var differential = Clamp(1 - (player.Popularity ?? PopularityFallback(player, userSeed, variantKey)));
    var positionBoost = player.Position.Equals("FWD", StringComparison.OrdinalIgnoreCase)
        ? 0.025
        : player.Position.Equals("MID", StringComparison.OrdinalIgnoreCase)
            ? 0.015
            : 0;
    var variety = StableNoise($"{userSeed}:{variantKey}:{player.Id}") * 0.025;

    return Clamp(
        recent * weights.RecentPerformance +
        minutes * weights.ExpectedPlayingTime +
        value * weights.PriceValue +
        fixture * weights.Fixture +
        consistency * weights.Consistency +
        differential * weights.Differential +
        positionBoost +
        variety);
  }

  private static IReadOnlyList<string> BuildSquadReasons(
      VariantDefinition variant,
      IReadOnlyList<FantasyRecommendationPlayer> players,
      int maxPoints,
      decimal budget)
  {
    var reasons = new List<string>
        {
            variant.Subtitle,
            $"Budget respecte: {players.Sum(player => player.Price):0.0}M / {budget:0.0}M.",
            "Blesses et suspendus exclus si l'information est fournie."
        };

    if (players.Count > 0 && players.Average(player => (double)player.Points / maxPoints) >= 0.55)
    {
      reasons.Add("Forme recente positive dans le groupe selectionne.");
    }

    return reasons;
  }

  private static IReadOnlyList<string> BuildReasons(
      FantasyRecommendationPlayer player,
      int maxPoints,
      decimal remainingBudget)
  {
    var reasons = new List<string> { "Compatible avec votre budget." };
    var value = ((double)player.Points + 4) / Math.Max(4, (double)player.Price);

    if (value >= 1.8) reasons.Add("Bon rapport points/prix.");
    if ((player.ExpectedPlayingTime ?? 0.78) >= 0.72) reasons.Add("Titulaire regulier.");
    if ((double)player.Points / maxPoints >= 0.55) reasons.Add("Forme recente positive.");
    if ((player.FixtureDifficulty ?? 0.5) <= 0.45) reasons.Add("Adversaire favorable.");
    if (player.Price <= remainingBudget * 0.72m) reasons.Add("Laisse de la marge pour completer l'effectif.");

    return reasons.Distinct().Take(4).ToList();
  }

  private static int PositionOrder(string position) => position.ToUpperInvariant() switch
  {
    "GK" => 0,
    "DEF" => 1,
    "MID" => 2,
    "FWD" => 3,
    _ => 9
  };

  private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));

  private static double PopularityFallback(FantasyRecommendationPlayer player, string userSeed, string variantKey)
  {
    var baseValue = 0.35 + Clamp(player.Points / 80.0) * 0.35;
    return Clamp(baseValue + StableNoise($"{userSeed}:pop:{variantKey}:{player.Club}") * 0.18);
  }

  private static double StableNoise(string value)
  {
    unchecked
    {
      var hash = 23;
      foreach (var ch in value)
      {
        hash = (hash * 31) + ch;
      }

      return Math.Abs(hash % 1000) / 1000.0;
    }
  }

  private sealed record VariantDefinition(
      string Key,
      string Title,
      string Subtitle,
      ScoreWeights Weights);

  private sealed record ScoreWeights(
      double RecentPerformance,
      double ExpectedPlayingTime,
      double PriceValue,
      double Fixture,
      double Consistency,
      double Differential);
}
