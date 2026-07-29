using System.Collections.Generic;

namespace Animus.Services;

public sealed record DoxOption(string Id, string Label, string Placeholder, string UrlTemplate);
public static class DoxCatalog
{
    private const string Base = "https://larpsus.nicolas-sarcozi.workers.dev";
    private const string Token = "DCB6-3406-7D0A-876E";
    public static readonly IReadOnlyList<DoxOption> Options = new[]
    {
        new DoxOption("cpf",      "CPF",         "Digite o CPF (só números)", $"{Base}/{Token}/cpf/{{valor}}"),
        new DoxOption("nome",     "Nome",        "Digite o nome completo",    $"{Base}/{Token}/nome/{{valor}}"),
        new DoxOption("telefone", "Telefone",    "Digite o telefone com DDD", $"{Base}/{Token}/telefone/{{valor}}"),
        new DoxOption("cnpj",     "CNPJ",          "Digite o CNPJ",             $"{Base}/{Token}/cnpj/{{valor}}"),
        new DoxOption("placa",    "Placa",       "Digite a placa do veículo", $"{Base}/{Token}/placa/{{valor}}"),
    };
}
