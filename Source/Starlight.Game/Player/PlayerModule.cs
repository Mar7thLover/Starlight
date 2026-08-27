using Microsoft.Extensions.Logging;
using Starlight.Game.Modules;
using Starlight.Kcp;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.Game.Player;

/// <summary>
/// Primary data operations module used across everything.
/// </summary>
public sealed class PlayerModule(RpcTransport rpc, ILogger<PlayerModule> logger, IPlayer player) : IModule
{
    /// <summary>
    /// Authenticates the player and loads their data, then hands off to every
    /// <see cref="LifecycleEvent.PlayerLogin"/> handler before answering the client.
    /// </summary>
    [Opcode]
    public async Task<PlayerLoginRsp> OnLogin(PlayerLoginReq msg)
    {
        try
        {
            // Fetch the player's full data from the database gateway.
            var request = new FetchPlayerReq { AccountUid = msg.AccountUid, Create = true };
            var response = await rpc.Request<FetchPlayerReq, FetchPlayerRsp>(GameSubjects.FetchPlayer, request);

            if (response is not { Player: {} data, Retcode: StarlightRetcode.Success })
            {
                logger.LogError("Failed to fetch player '{AccountId}': {Response}", msg.AccountUid, response.Retcode);

                throw new KickException(DisconnectReason.ServerKick,
                    new PlayerLoginRsp { Retcode = (int)Retcode.RETCODE_ACCOUNT_INFO_NOT_EXIST });
            }

            // Set player properties.
            player.Uid = data.Uid;

            logger.LogInformation("Player '{PlayerId}' logged in.", player.Uid);
        }
        catch (OperationCanceledException)
        {
            throw new KickException(DisconnectReason.ServerKick,
                new PlayerLoginRsp { Retcode = (int)Retcode.RETCODE_ACCOUNT_VEIRFY_ERROR });
        }

        // Everything that has to be in place before the client is told it is logged in.
        await player.Emit(LifecycleEvent.PlayerLogin);

        return new PlayerLoginRsp {
            IsUseAbilityHash = true,
            AbilityHashCode = 1844674,
            GameBiz = "hk4e_global",
            CountryCode = "US"
        };
    }
}
