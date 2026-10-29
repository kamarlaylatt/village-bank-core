namespace VillageBank.Api.Domain;

public record LineInput(int AccountId, decimal Debit, decimal Credit);
