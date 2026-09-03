using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sunset.Application.Interfaces;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence;

/// <summary>
/// Fills an empty database with fictitious data for local development/testing.
/// No-op if any user already exists, so it's safe to run on every startup.
/// </summary>
public static class DbSeeder
{
    private const string SeedPassword = "Password123!";

    private static readonly (string Name, string Email)[] UserSeeds =
    [
        ("Beatriz Almeida", "beatriz@sunsetapp.dev"),
        ("Rafael Souza", "rafael@sunsetapp.dev"),
        ("Camila Ferreira", "camila@sunsetapp.dev"),
        ("Lucas Martins", "lucas@sunsetapp.dev"),
        ("Juliana Costa", "juliana@sunsetapp.dev"),
        ("Pedro Henrique", "pedro@sunsetapp.dev"),
        ("Mariana Lima", "mariana@sunsetapp.dev"),
        ("Thiago Rocha", "thiago@sunsetapp.dev"),
    ];

    private static readonly (string Name, double Lat, double Lng, string City)[] LocationSeeds =
    [
        ("Praia do Rosa", -28.1305, -48.6224, "Imbituba"),
        ("Praia de Jericoacoara", -2.7947, -40.5148, "Jijoca de Jericoacoara"),
        ("Lagoa da Conceição", -27.6081, -48.4652, "Florianópolis"),
        ("Vale da Lua", -14.1289, -47.5951, "Alto Paraíso de Goiás"),
        ("Praia do Sancho", -3.8623, -32.4370, "Fernando de Noronha"),
        ("Praia do Farol", -22.9661, -42.0278, "Arraial do Cabo"),
    ];

    // (location index, author index, caption)
    private static readonly (int LocationIndex, int UserIndex, string Caption)[] PhotoSeeds =
    [
        (0, 0, "O pôr do sol mais bonito que já vi por aqui!"),
        (0, 3, "Voltando sempre pra ver esse horizonte."),
        (1, 2, "Subimos a Duna do Pôr do Sol e valeu cada passo."),
        (1, 7, "Impossível não parar tudo pra ver isso."),
        (2, 1, "Fim de tarde perfeito na lagoa."),
        (2, 6, "Point favorito pra encontrar os amigos no fim do dia."),
        (3, 4, "Paisagem lunar com um entardecer surreal."),
        (3, 5, "As rochas esculpidas pela água ficam ainda mais bonitas nessa luz."),
        (4, 6, "Já entendi por que elegeram essa praia uma das mais bonitas do mundo."),
        (4, 3, "Vista de tirar o fôlego, sem exagero."),
        (5, 1, "Água cristalina e um pôr do sol de cinema."),
        (5, 2, "Bate e volta que vale muito a pena."),
    ];

    private static readonly string[] CommentPool =
    [
        "Que vista incrível!",
        "Preciso conhecer esse lugar.",
        "As cores desse céu são de outro mundo.",
        "Já salvei pra próxima viagem!",
        "Perfeito para fechar o dia.",
        "Isso não parece real.",
        "Um dos melhores pores do sol que já vi por aqui.",
        "Marca aí a próxima vez que for!",
        "Que sorte poder ver isso ao vivo.",
        "Poesia pura.",
    ];

    public static async Task SeedAsync(SunsetDbContext context, IPasswordHasher passwordHasher, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (await context.Users.AnyAsync(u => u.Email == UserSeeds[0].Email, cancellationToken))
            return;

        var random = new Random(42);
        var now = DateTime.UtcNow;

        var passwordHash = passwordHasher.Hash(SeedPassword);
        var users = UserSeeds
            .Select(u => new User(u.Name, u.Email, passwordHash, $"https://i.pravatar.cc/300?u={u.Email}"))
            .ToList();
        for (var i = 0; i < users.Count; i++)
            SetCreatedAt(users[i], now.AddDays(-random.Next(15, 200)));

        var locations = LocationSeeds
            .Select(l => new Location(l.Name, l.Lat, l.Lng, l.City))
            .ToList();
        for (var i = 0; i < locations.Count; i++)
            SetCreatedAt(locations[i], now.AddDays(-random.Next(60, 300)));

        var photos = new List<Photo>();
        foreach (var seed in PhotoSeeds)
        {
            var author = users[seed.UserIndex];
            var location = locations[seed.LocationIndex];
            var photo = new Photo(author.Id, location.Id, $"https://picsum.photos/seed/sunset-{photos.Count}/1200/800", seed.Caption);
            SetCreatedAt(photo, now.AddDays(-random.Next(1, 60)));
            photos.Add(photo);
        }

        var likes = new List<Like>();
        var comments = new List<Comment>();
        foreach (var photo in photos)
        {
            var others = users.Where(u => u.Id != photo.UserId).OrderBy(_ => random.Next()).ToList();

            foreach (var liker in others.Take(random.Next(2, 6)))
            {
                photo.IncrementLikes();
                var like = new Like(liker.Id, photo.Id);
                SetCreatedAt(like, photo.CreatedAt.AddDays(random.Next(0, 10)));
                likes.Add(like);
            }

            foreach (var commenter in others.Take(random.Next(1, 4)))
            {
                var comment = new Comment(commenter.Id, photo.Id, CommentPool[random.Next(CommentPool.Length)]);
                SetCreatedAt(comment, photo.CreatedAt.AddDays(random.Next(0, 10)));
                comments.Add(comment);
            }
        }

        // Ratings are spread across week/month/all-time windows so GET /locations/ranking
        // returns different results per period, instead of the three periods looking identical.
        var ratings = new List<Rating>();
        foreach (var location in locations)
        {
            var raters = users.OrderBy(_ => random.Next()).Take(random.Next(4, 8)).ToList();
            var scores = new List<int>();
            var daysAgoOptions = new[] { random.Next(1, 6), random.Next(10, 25), random.Next(40, 90) };

            for (var i = 0; i < raters.Count; i++)
            {
                var score = random.Next(0, 10) < 8 ? random.Next(4, 6) : random.Next(2, 4);
                var rating = new Rating(raters[i].Id, location.Id, score);
                SetCreatedAt(rating, now.AddDays(-daysAgoOptions[i % daysAgoOptions.Length]));
                ratings.Add(rating);
                scores.Add(score);
            }

            location.RecalculateAvgRating(Math.Round((decimal)scores.Average(), 2));
        }

        context.Users.AddRange(users);
        context.Locations.AddRange(locations);
        context.Photos.AddRange(photos);
        context.Likes.AddRange(likes);
        context.Comments.AddRange(comments);
        context.Ratings.AddRange(ratings);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded database with {Users} users, {Locations} locations, {Photos} photos, {Likes} likes, {Comments} comments, {Ratings} ratings. All seed users share the password '{Password}'.",
            users.Count, locations.Count, photos.Count, likes.Count, comments.Count, ratings.Count, SeedPassword);
    }

    private static void SetCreatedAt(BaseEntity entity, DateTime createdAt) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))!.SetValue(entity, createdAt);
}
