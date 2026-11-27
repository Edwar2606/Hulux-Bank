using HuluxApi;
using HuluxApi.Models;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<FreebaseService>();

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "No se encontró la cadena de conexión a MySQL");

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("VuePolicy",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

app.UseCors("VuePolicy");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/Hulux", () =>
{
    return "Hola Edwar";
});

app.MapGet("/clientes", () =>
{

    using var connection = new NpgsqlConnection(connectionString);

    connection.Open();

    using var command = new NpgsqlCommand(
        "SELECT idClients, Nombre_Cliente, Documento FROM clients",
        connection);

    using var reader = command.ExecuteReader();

    var clientes = new List<string>();

    while (reader.Read())
    {
        clientes.Add(string.Format("idClients: {0}, Nombre_Cliente: {1}, Documento: {2}", reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
    }

    return clientes;
});

app.MapPost("/clients", (ClientRequest cliente) =>
{
    Console.WriteLine("POST RECIBIDO");
    Console.WriteLine(cliente.Nombre_Cliente);
    Console.WriteLine(cliente.Documento);


    using var connection = new NpgsqlConnection(connectionString);

    connection.Open();

    using var transaction = connection.BeginTransaction();

    try 
    {
        using var verificar = new NpgsqlCommand(
            "SELECT COUNT(*) FROM clients WHERE Documento = @Documento",
            connection,
            transaction);

        verificar.Parameters.AddWithValue(
    "@Documento",
    cliente.Documento);

    var existe = Convert.ToInt32(
    verificar.ExecuteScalar());

    if (existe > 0)
    {
    return "Ya existe un cliente con ese documento";
    }

       using var commandClient = new NpgsqlCommand(
@"INSERT INTO clients
(Nombre_Cliente, Documento)
VALUES
(@Nombre_Cliente, @Documento)
RETURNING idClients",
connection,
transaction);

commandClient.Parameters.AddWithValue(
    "@Nombre_Cliente",
    cliente.Nombre_Cliente);

commandClient.Parameters.AddWithValue(
    "@Documento",
    cliente.Documento);

var clientId =
    Convert.ToInt32(
        commandClient.ExecuteScalar());

        using var commandAccount = new NpgsqlCommand(
            "INSERT INTO accounts (Contraseña, Saldo, Clients_idClients) VALUES (@Contraseña, @Saldo, @Clients_idClients)",
            connection,
            transaction);
        commandAccount.Parameters.AddWithValue(
    "@Contraseña",
    cliente.Contraseña);
        commandAccount.Parameters.AddWithValue("@Saldo", 0.00);
        commandAccount.Parameters.AddWithValue("@Clients_idClients", clientId);

    commandAccount.ExecuteNonQuery();

transaction.Commit();

return $"Cliente y cuenta creados correctamente para: {cliente.Nombre_Cliente}";

    }
catch (Exception ex)
{
    transaction.Rollback();

    return ex.ToString();
}
});
app.MapGet("/Cuentas", () =>
{
    return ("Lista de Cuentas");
});

app.MapPost("/deposito", (TransferRequest datos) =>
{

    using var connection = new NpgsqlConnection(connectionString);

    connection.Open();

    using var transaction = connection.BeginTransaction();

    try
    {
        if (datos.Monto <= 0)
        {
            return Results.BadRequest("Monto inválido");
        }

        using var actualizarSaldo = new NpgsqlCommand(
        @"UPDATE accounts
          SET Saldo = Saldo + @Monto
          WHERE Clients_idClients = @ClienteOrigen",
        connection,
        transaction);

        actualizarSaldo.Parameters.AddWithValue(
            "@Monto",
            datos.Monto);

        actualizarSaldo.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        actualizarSaldo.ExecuteNonQuery();

        using var movimiento = new NpgsqlCommand(
        @"INSERT INTO transactions
        (Tipo, Monto, Descripcion, Accounts_idAccounts)
        VALUES
        (
            'Deposito',
            @Monto,
            'Deposito realizado',
            (
                SELECT idAccounts
                FROM accounts
                WHERE Clients_idClients = @ClienteOrigen
            )
        )",
        connection,
        transaction);

        movimiento.Parameters.AddWithValue(
            "@Monto",
            datos.Monto);

        movimiento.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        movimiento.ExecuteNonQuery();

        using var saldoActual = new NpgsqlCommand(
        @"SELECT Saldo
          FROM accounts
          WHERE Clients_idClients = @ClienteOrigen",
        connection,
        transaction);

        saldoActual.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        var nuevoSaldo =
            Convert.ToDouble(
                saldoActual.ExecuteScalar());

        transaction.Commit();

        return Results.Ok(new
        {
            Mensaje = "Depósito realizado correctamente",
            Saldo = nuevoSaldo
        });
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return Results.BadRequest(ex.Message);
    }
});
app.MapPost("/retiro", (TransferRequest datos) =>
{

    using var connection = new NpgsqlConnection(connectionString);

    connection.Open();

    using var transaction = connection.BeginTransaction();

    try
    {
        if (datos.Monto <= 0)
        {
            return Results.BadRequest("Monto inválido");
        }

        using var consultarSaldo = new NpgsqlCommand(
        @"SELECT Saldo
          FROM accounts
          WHERE Clients_idClients = @ClienteOrigen",
        connection,
        transaction);

        consultarSaldo.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        var saldo =
            Convert.ToDouble(
                consultarSaldo.ExecuteScalar());

        if (saldo < datos.Monto)
        {
            return Results.BadRequest("Saldo insuficiente");
        }

        using var descontar = new NpgsqlCommand(
        @"UPDATE accounts
          SET Saldo = Saldo - @Monto
          WHERE Clients_idClients = @ClienteOrigen",
        connection,
        transaction);

        descontar.Parameters.AddWithValue(
            "@Monto",
            datos.Monto);

        descontar.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        descontar.ExecuteNonQuery();

        using var movimiento = new NpgsqlCommand(
        @"INSERT INTO transactions
        (Tipo, Monto, Descripcion, Accounts_idAccounts)
        VALUES
        (
            'Retiro',
            @Monto,
            'Retiro realizado',
            (
                SELECT idAccounts
                FROM accounts
                WHERE Clients_idClients = @ClienteOrigen
            )
        )",
        connection,
        transaction);

        movimiento.Parameters.AddWithValue(
            "@Monto",
            datos.Monto);

        movimiento.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        movimiento.ExecuteNonQuery();

        using var saldoActual = new NpgsqlCommand(
        @"SELECT Saldo
          FROM accounts
          WHERE Clients_idClients = @ClienteOrigen",
        connection,
        transaction);

        saldoActual.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        var nuevoSaldo =
            Convert.ToDouble(
                saldoActual.ExecuteScalar());

        transaction.Commit();

        return Results.Ok(new
        {
            Mensaje = "Retiro realizado correctamente",
            Saldo = nuevoSaldo
        });
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return Results.BadRequest(ex.Message);
    }
});

app.MapPost("/transferencia", (TransferRequest datos) =>
{

    using var connection = new NpgsqlConnection(connectionString);

    connection.Open();

    using var transaction = connection.BeginTransaction();

    try
    {
        if (datos.Monto <= 0)
        {
            return Results.BadRequest("Monto inválido");
        }

        using var buscarDestino = new NpgsqlCommand(
        @"SELECT idClients
          FROM clients
          WHERE Documento = @DocumentoDestino",
        connection,
        transaction);

        buscarDestino.Parameters.AddWithValue(
            "@DocumentoDestino",
            datos.DocumentoDestino);

        var destino = buscarDestino.ExecuteScalar();

        if (destino == null)
        {
            return Results.BadRequest("Cliente destino no existe");
        }

        int clienteDestino = Convert.ToInt32(destino);

        using var consultarSaldo = new NpgsqlCommand(
        @"SELECT Saldo
          FROM accounts
          WHERE Clients_idClients = @ClienteOrigen",
        connection,
        transaction);

        consultarSaldo.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        double saldo = Convert.ToDouble(
            consultarSaldo.ExecuteScalar());

        if (saldo < datos.Monto)
        {
            return Results.BadRequest("Saldo insuficiente");
        }

        using var descontar = new NpgsqlCommand(
        @"UPDATE accounts
          SET Saldo = Saldo - @Monto
          WHERE Clients_idClients = @ClienteOrigen",
        connection,
        transaction);

        descontar.Parameters.AddWithValue(
            "@Monto",
            datos.Monto);

        descontar.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        descontar.ExecuteNonQuery();

        using var sumar = new NpgsqlCommand(
        @"UPDATE accounts
          SET Saldo = Saldo + @Monto
          WHERE Clients_idClients = @ClienteDestino",
        connection,
        transaction);

        sumar.Parameters.AddWithValue(
            "@Monto",
            datos.Monto);

        sumar.Parameters.AddWithValue(
            "@ClienteDestino",
            clienteDestino);

        sumar.ExecuteNonQuery();

        using var movimientoSalida = new NpgsqlCommand(
        @"INSERT INTO transactions
        (Tipo, Monto, Descripcion, Accounts_idAccounts)
        VALUES
        (
            'Transferencia Enviada',
            @Monto,
            'Transferencia realizada',
            (
                SELECT idAccounts
                FROM accounts
                WHERE Clients_idClients = @ClienteOrigen
            )
        )",
        connection,
        transaction);

        movimientoSalida.Parameters.AddWithValue(
            "@Monto",
            datos.Monto);

        movimientoSalida.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        movimientoSalida.ExecuteNonQuery();

        using var movimientoEntrada = new NpgsqlCommand(
        @"INSERT INTO transactions
        (Tipo, Monto, Descripcion, Accounts_idAccounts)
        VALUES
        (
            'Transferencia Recibida',
            @Monto,
            'Transferencia recibida',
            (
                SELECT idAccounts
                FROM accounts
                WHERE Clients_idClients = @ClienteDestino
            )
        )",
        connection,
        transaction);

        movimientoEntrada.Parameters.AddWithValue(
            "@Monto",
            datos.Monto);

        movimientoEntrada.Parameters.AddWithValue(
            "@ClienteDestino",
            clienteDestino);

        movimientoEntrada.ExecuteNonQuery();

        using var consultarNuevoSaldo = new NpgsqlCommand(
        @"SELECT Saldo
          FROM accounts
          WHERE Clients_idClients = @ClienteOrigen",
        connection,
        transaction);

        consultarNuevoSaldo.Parameters.AddWithValue(
            "@ClienteOrigen",
            datos.ClienteOrigen);

        var nuevoSaldo = Convert.ToDouble(
            consultarNuevoSaldo.ExecuteScalar());

        transaction.Commit();

        return Results.Ok(new
        {
            Mensaje = "Transferencia realizada correctamente",
            Saldo = nuevoSaldo
        });
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return Results.BadRequest(ex.Message);
    }
});

app.MapPost("/login", (LoginRequest login) =>
{

    using var connection = new NpgsqlConnection(connectionString);

    connection.Open();

    using var command = new NpgsqlCommand(
@"SELECT
    c.idClients,
    c.Nombre_Cliente,
    a.Saldo
FROM clients c
INNER JOIN accounts a
ON c.idClients = a.Clients_idClients
WHERE c.Documento = @Documento
AND a.Contraseña = @Contraseña",
    connection);

    command.Parameters.AddWithValue(
        "@Documento",
        login.Documento);

    command.Parameters.AddWithValue(
        "@Contraseña",
        login.Contraseña);

    using var reader = command.ExecuteReader();

    if (!reader.Read())
    {
        return Results.BadRequest("Datos incorrectos");
    }

    return Results.Ok(new
{
    ClientId = reader.GetInt32(0),
    Nombre = reader.GetString(1),
    Saldo = reader.GetDouble(2)
    });
});

app.MapGet("/transacciones/{clientId}", (int clientId) =>
{

    using var connection =
        new NpgsqlConnection(connectionString);

    connection.Open();

    using var command = new NpgsqlCommand(
    @"SELECT
        Tipo,
        Monto,
        Descripcion
      FROM transactions t
      INNER JOIN accounts a
      ON t.Accounts_idAccounts = a.idAccounts
      WHERE a.Clients_idClients = @ClientId
      ORDER BY t.idTransaction DESC",
    connection);

    command.Parameters.AddWithValue(
        "@ClientId",
        clientId);

    using var reader = command.ExecuteReader();

    var movimientos = new List<object>();

    while (reader.Read())
    {
        movimientos.Add(new
        {
            Tipo = reader.GetString(0),
            Monto = reader.GetDecimal(1),
            Descripcion = reader.GetString(2)
        });
    }

    return movimientos;
});

app.Run();
