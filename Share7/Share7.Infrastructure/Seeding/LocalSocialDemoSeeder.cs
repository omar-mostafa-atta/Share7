using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Share7.Application.BrainPass;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Social;
using Share7.Domain.BrainPass;
using Share7.Domain.Constants;
using Share7.Domain.Entities;
using Share7.Domain.Leaderboards;
using Share7.Domain.Rewards;
using Share7.Domain.Social;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Seeding;

/// <summary>Opt-in local fixtures, created through the ordinary policy and authoring services.
/// No claims, ranked wins, consent grants or fulfilment are manufactured.</summary>
public static class LocalSocialDemoSeeder
{
    public static async Task RunAsync(IServiceProvider services, string password, CancellationToken token = default)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        if (!db.Database.IsSqlServer() || db.Database.GetDbConnection().Database != "Share7_Development"
            || !new[] { @".\SQLEXPRESS", @"localhost\SQLEXPRESS", @"127.0.0.1\SQLEXPRESS" }
                .Contains(db.Database.GetDbConnection().DataSource, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Local social fixtures require the named local development database.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 20)
            throw new InvalidOperationException("Provide a generated local fixture credential.");

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var profiles = services.GetRequiredService<ISocialProfileAdminService>();
        var grade = await db.Grades.OrderBy(g => g.Id).Select(g => g.Id).FirstAsync(token);
        var now = DateTime.UtcNow;
        var people = new[] {
            ("anan", "Anan", "عنان", OfficialProfileKind.Creator, "Chemistry and labs", "الكيمياء والمختبرات"),
            ("mody", "Mody", "مودي", OfficialProfileKind.Developer, "Game developer", "مطوّر الألعاب"),
            ("saif", "Saif", "سيف", OfficialProfileKind.Designer, "3D artist and project manager", "فنان ثلاثي الأبعاد ومدير المشروع"),
            ("kero", "KERO", "كيرو", OfficialProfileKind.Designer, "3D artist", "فنان ثلاثي الأبعاد"),
            ("omar", "Omar", "عمر", OfficialProfileKind.Developer, "Backend server developer", "مطوّر الخادم")
        };
        foreach (var person in people)
        {
            Guid user = await Account("demo.team." + person.Item1, "Local-" + person.Item2);
            if (!await db.OfficialProfiles.AnyAsync(p => p.UserId == user, token))
                Check(await profiles.SetIdentityAsync(user, new(person.Item4,true,true,person.Item2,person.Item3,person.Item5,person.Item6),token));
            if (!await db.OfficialActivities.AnyAsync(a => a.UserId == user && a.PublicationKey == "local-welcome-v1",token))
                Check(await profiles.PublishActivityAsync(user,new("local-welcome-v1","Explore our local showcase","اكتشف معرضنا المحلي",null,now,now.AddDays(14)),token));
        }
        // Only the actual student placeholder is offered to regular customizable profiles.
        Check(await profiles.SetContentAsync(new("local.student.scene.v1",ShowcaseContentKind.Scene,"showcase.placeholder.student",null,null,false,true,0,1),token));

        var alpha = await Account("demo.social.alpha","Local-Falcon");
        var beta = await Account("demo.social.beta","Local-Fox");
        var gamma = await Account("demo.social.gamma","Local-Owl");
        var friends = services.GetRequiredService<IFriendService>();
        if (!await services.GetRequiredService<IFriendGraph>().AreFriendsAsync(alpha,beta,token))
        {
            var code = Value(await friends.GetCodeAsync(beta,token));
            var request = Value(await friends.AddByCodeAsync(alpha,new AddFriendRequest { Code=code.Code },token));
            Value(await friends.AcceptAsync(beta,request.Id,token));
        }
        if (!await services.GetRequiredService<IFriendGraph>().AreFriendsAsync(alpha,gamma,token))
        {
            var code = Value(await friends.GetCodeAsync(gamma,token));
            Value(await friends.AddByCodeAsync(alpha,new AddFriendRequest { Code=code.Code },token));
        }
        var teams = services.GetRequiredService<IPlayerTeamService>();
        var team = Value(await teams.CreateAsync(alpha,"local-demo-team-v1",token));
        if (!team.Members.Any(m => m.UserId==beta && m.Accepted))
        { Check(await teams.InviteAsync(alpha,team.Id,beta,token)); Check(await teams.AcceptAsync(beta,team.Id,token)); }
        var party = Value(await services.GetRequiredService<IPartyService>().CreateAsync(alpha,token));
        if (!party.Members.Any(m => m.UserId==beta))
        {
            var invited = Value(await services.GetRequiredService<IPartyService>().InviteAsync(alpha,party.Id,new() { UserId=beta },token));
            Value(await services.GetRequiredService<IPartyService>().AcceptInviteAsync(beta,invited.Id,token));
        }

        if (!await db.BrainPassSeasons.AnyAsync(s => s.Key=="local-discovery-v1",token))
        {
            var coins = await db.Currencies.SingleAsync(c => c.Key=="coins",token);
            var tiers = new List<BrainPassTierInput>();
            for (int i=1;i<=5;i++)
            {
                var id=SeedId.For("local-brainpass-reward",i.ToString());
                if (!await db.RewardRules.AnyAsync(r=>r.Id==id,token))
                    db.RewardRules.Add(new RewardRule { Id=id,Name=$"Local Brain Pass tier {i}",EventType=RewardEventType.BrainPassTier,
                        RepeatPolicy=RewardRepeatPolicy.Once,Enabled=true,CreatedAtUtc=now,UpdatedAtUtc=now,
                        Grants=new List<RewardRuleGrant> { new() { Id=SeedId.For("local-brainpass-grant",i.ToString()),CurrencyId=coins.Id,Amount=100*i } } });
                tiers.Add(new(i,BrainPassTrack.Free,50*i,id));
            }
            await db.SaveChangesAsync(token);
            var admin = services.GetRequiredService<IBrainPassAdminService>();
            var draft = Value(await admin.SaveAsync(null,new("local-discovery-v1","Season of discovery","موسم الاكتشاف",
                now.AddMinutes(1),now.AddDays(28),now.AddDays(35),null,null,
                new[] { new BrainPassRuleInput("LESSONS_ACED",1,1,50,50,200) },tiers),token));
            Value(await admin.PublishAsync(draft.Id,draft.Version,token));
        }
        return;

        async Task<Guid> Account(string name,string handle)
        {
            var user=await users.FindByNameAsync(name);
            if (user==null)
            {
                user=new ApplicationUser { Id=SeedId.For("local-social-account",name),UserName=name,Email=name+"@example.invalid",EmailConfirmed=true,CreatedAt=now };
                var result=await users.CreateAsync(user,password);
                if (!result.Succeeded) throw new InvalidOperationException("Could not create a synthetic local fixture account.");
                await users.AddToRoleAsync(user,Roles.Student);
            }
            if (!await db.StudentProfiles.AnyAsync(p=>p.UserId==user.Id,token))
                db.StudentProfiles.Add(new StudentProfile { Id=SeedId.For("local-social-profile",name),UserId=user.Id,FullName="Local fixture",Age=25,GradeId=grade,PhoneNumber="",CreatedAt=now });
            if (!await db.PlayerDisplayNames.AnyAsync(p=>p.UserId==user.Id,token))
                db.PlayerDisplayNames.Add(new PlayerDisplayName { UserId=user.Id,Handle=handle,Source=DisplayNameSource.Generated,CreatedAtUtc=now });
            await db.SaveChangesAsync(token);
            return user.Id;
        }
    }
    private static void Check(ServiceResult result)
    { if (!result.Succeeded) throw new InvalidOperationException("Local social fixture authoring failed: "+string.Join("; ",result.Errors)); }
    private static T Value<T>(ServiceResult<T> result)
    { Check(result);return result.Value!; }
}
