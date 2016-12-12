﻿using NBitcoin;
using NBitcoin.RPC;
using NTumbleBit.PuzzleSolver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

#if !CLIENT
namespace NTumbleBit.TumblerServer.Services.RPCServices
#else
namespace NTumbleBit.Client.Tumbler.Services.RPCServices
#endif
{
	public class RPCTrustedBroadcastService : ITrustedBroadcastService
	{
		public RPCTrustedBroadcastService(RPCClient rpc)
		{
			if(rpc == null)
				throw new ArgumentNullException("rpc");
			_RPCClient = rpc;
			_Broadcaster = new RPCBroadcastService(rpc);
		}

		RPCBroadcastService _Broadcaster;

		private readonly RPCClient _RPCClient;
		public RPCClient RPCClient
		{
			get
			{
				return _RPCClient;
			}
		}

		List<TrustedBroadcastRequest> _Broadcasts = new List<TrustedBroadcastRequest>();
		public void Broadcast(TrustedBroadcastRequest broadcast)
		{
			if(broadcast == null)
				throw new ArgumentNullException("broadcast");
			var address = broadcast.PreviousScriptPubKey.GetDestinationAddress(RPCClient.Network);
			if(address == null)
				throw new NotSupportedException("ScriptPubKey to track not supported");
			RPCClient.ImportAddress(address, "", false);
			_Broadcasts.Add(broadcast);
			_Broadcaster.Broadcast(broadcast.Transaction);
		}

		public Transaction[] TryBroadcast()
		{
			var height = RPCClient.GetBlockCount();
			List<Transaction> broadcasted = new List<Transaction>();
			foreach(var broadcast in _Broadcasts)
			{
				if(height < broadcast.BroadcastAt.Height)
					continue;

				foreach(var tx in GetReceivedTransactions(broadcast.PreviousScriptPubKey))
				{
					foreach(var coin in tx.Outputs.AsCoins())
					{
						if(coin.ScriptPubKey == broadcast.PreviousScriptPubKey)
						{
							var transaction = broadcast.ReSign(coin);
							_Broadcaster.Broadcast(transaction);
						}
					}
				}
			}
			return broadcasted.ToArray();
		}
		

		public Transaction[] GetReceivedTransactions(Script scriptPubKey)
		{
			if(scriptPubKey == null)
				throw new ArgumentNullException("scriptPubKey");

			var address = scriptPubKey.GetDestinationAddress(RPCClient.Network);
			if(address == null)
				return new Transaction[0];


			var result = RPCClient.SendCommand("listtransactions", "", 100, 0, true);
			if(result.Error != null)
				return null;

			var transactions = (Newtonsoft.Json.Linq.JArray)result.Result;
			List<TransactionInformation> results = new List<TransactionInformation>();
			foreach(var obj in transactions)
			{
				var txId = new uint256((string)obj["txid"]);
				var tx = GetTransaction(txId);
				if((string)obj["address"] == address.ToString())
				{
					results.Add(tx);
				}
			}
			return results.Select(t => t.Transaction).ToArray();
		}

		public TransactionInformation GetTransaction(uint256 txId)
		{
			var result = RPCClient.SendCommand("getrawtransaction", txId.ToString(), 1);
			if(result == null || result.Error != null)
				return null;
			var tx = new Transaction((string)result.Result["hex"]);
			var confirmations = result.Result["confirmations"];
			return new TransactionInformation()
			{
				Confirmations = confirmations == null ? 0 : (int)confirmations,
				Transaction = tx
			};
		}
	}


}
