namespace HuluxApi.Models
{
    public class Accounts
    {
        public int IdAccounts { get; set; }

        public string Contraseña{ get; set; } = string.Empty;

        public decimal Saldo { get; set; } = 0.00m;
        public int Clients_idClients { get; set; } = 0;
    }
}