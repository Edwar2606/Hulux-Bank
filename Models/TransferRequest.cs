namespace HuluxApi.Models
{
    public class TransferRequest
    {
        public int ClienteOrigen { get; set; }

        public string DocumentoDestino { get; set; } = "";

        public double Monto { get; set; }
    }
}