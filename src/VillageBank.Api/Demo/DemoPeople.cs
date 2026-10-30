namespace VillageBank.Api.Demo;

public static class DemoPeople
{
    // Lao towns. They go into the member's "village" field.
    public static readonly string[] Towns =
    {
        "Vientiane", "Luang Prabang", "Pakse", "Savannakhet", "Thakhek", "Phonsavan"
    };

    public static readonly string[] Names =
    {
        "Khamla Sisavath", "Bounmy Keomany", "Souksavanh Vongsa", "Phonexay Douangdala",
        "Viengkham Phommachanh", "Noy Sayavong", "Seng Chanthavong", "Thongchanh Xayavong",
        "Manivone Luangrath", "Somphone Inthavong", "Keo Phetsavong", "Dalavanh Souvannavong",
        "Latsamy Boupha", "Vilayvanh Manivong", "Sengdao Phimmasone", "Anousone Vilaysack",
        "Malaythong Khounnavong", "Phoutthasone Sengdara", "Bounthavy Chanthala", "Khampheng Vongphachanh",
        "Soudaphone Lattanavong", "Vanida Phommasathit", "Sivilay Rattanavong", "Chanthaly Insisiengmay",
        "Outhai Keophilavong", "Boualy Sihalath", "Sonexay Phengphachanh", "Thipphavanh Xaiyalath",
        "Viengxay Soukhaseum", "Daovieng Mounivong"
    };

    public static string Town(int index) => Towns[index % Towns.Length];

    // Obviously fake numbers, so a real person is never phoned by mistake.
    public static string Phone(int index) => $"020 0000 {index + 1:D4}";
}
