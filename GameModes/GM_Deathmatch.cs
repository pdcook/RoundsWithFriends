﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnboundLib;
using UnboundLib.Networking;
using UnboundLib.GameModes;
using Sonigon;

namespace RWF.GameModes
{
	public class GM_Deathmatch : MonoBehaviour
	{
		public static GM_Deathmatch instance;

		private Dictionary<int, int> teamPoints = new Dictionary<int, int>();
		private Dictionary<int, int> teamRounds = new Dictionary<int, int>();
		private Dictionary<int, bool> waitingForPlayer = new Dictionary<int, bool>();
		private bool isTransitioning;
		private int playersNeededToStart = 2;
		private int currentWinningTeamID = -1;

		private void Awake() {
			GM_Deathmatch.instance = this;
		}

		private void Start() {
			GameModeManager.TriggerHook(GameModeHooks.HookInitStart);

			PlayerManager.instance.SetPlayersSimulated(false);
			PlayerAssigner.instance.maxPlayers = this.playersNeededToStart;

			this.playersNeededToStart = RWFMod.instance.MinPlayers;
			PlayerAssigner.instance.maxPlayers = RWFMod.instance.MaxPlayers;

			GameModeManager.TriggerHook(GameModeHooks.HookInitEnd);
		}

		[UnboundRPC]
		public static void RPCO_RequestSyncUp(int requestingPlayer) {
			int playerID = PlayerManager.instance.players.Find(p => p.data.view.IsMine).playerID;
			NetworkingManager.RPC(typeof(GM_Deathmatch), nameof(GM_Deathmatch.RPCM_ReturnSyncUp), requestingPlayer, playerID);
		}

		[UnboundRPC]
		public static void RPCM_ReturnSyncUp(int requestingPlayer, int readyPlayer) {
			int myPlayerID = PlayerManager.instance.players.Find(p => p.data.view.IsMine).playerID;
			if (myPlayerID == requestingPlayer) {
				GM_Deathmatch.instance.waitingForPlayer[readyPlayer] = false;
			}
		}

		private IEnumerator WaitForSyncUp() {
			if (PhotonNetwork.OfflineMode) {
				yield break;
			}

			foreach (var player in PlayerManager.instance.players) {
				this.waitingForPlayer[player.playerID] = true;
			}

			int myPlayerID = PlayerManager.instance.players.Find(p => p.data.view.IsMine).playerID;
			NetworkingManager.RPC(typeof(GM_Deathmatch), nameof(GM_Deathmatch.RPCO_RequestSyncUp), myPlayerID);

			while (this.waitingForPlayer.Values.ToList().Any(isWaiting => isWaiting)) {
				yield return null;
			}
		}

		public void PlayerJoined(Player player) {
			this.waitingForPlayer.Add(player.playerID, false);
			this.teamPoints.Add(player.teamID, 0);
			this.teamRounds.Add(player.teamID, 0);
		}

		public void PlayerDied(Player killedPlayer, int playersAlive) {
			if (playersAlive == 1) {
				TimeHandler.instance.DoSlowDown();

				if (PhotonNetwork.IsMasterClient) {
					NetworkingManager.RPC(
						typeof(GM_Deathmatch),
						nameof(GM_Deathmatch.RPCA_NextRound),
						PlayerManager.instance.GetLastPlayerAlive().teamID,
						this.teamPoints,
						this.teamRounds
					);
				}
			}
		}

		public void StartGame() {
			if (GameManager.instance.isPlaying) {
				return;
			}

			GameManager.instance.isPlaying = true;
			this.StartCoroutine(this.DoStartGame());
		}

		private IEnumerator DoStartGame() {
			GameModeManager.TriggerHook(GameModeHooks.HookGameStart);

			CardBarHandler.instance.Rebuild();
			UIHandler.instance.InvokeMethod("SetNumberOfRounds", (int) GameModeManager.CurrentHandler.Settings["roundsToWinGame"]);
			ArtHandler.instance.NextArt();

			GameManager.instance.battleOngoing = false;

			UIHandler.instance.ShowJoinGameText("LETS GOO!", PlayerSkinBank.GetPlayerSkinColors(1).winText);
			yield return new WaitForSeconds(0.25f);
			UIHandler.instance.HideJoinGameText();

			PlayerManager.instance.SetPlayersSimulated(false);
			PlayerManager.instance.InvokeMethod("SetPlayersVisible", false);
			MapManager.instance.LoadNextLevel(false, false);
			TimeHandler.instance.DoSpeedUp();

			yield return new WaitForSecondsRealtime(1f);

			GameModeManager.TriggerHook(GameModeHooks.HookPickStart);

			for (int i = 0; i < PlayerManager.instance.players.Count; i++) {
				yield return this.WaitForSyncUp();

				GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickStart);

				CardChoiceVisuals.instance.Show(i, true);
				yield return CardChoice.instance.DoPick(1, PlayerManager.instance.players[i].playerID, PickerType.Player);

				GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickEnd);

				yield return new WaitForSecondsRealtime(0.1f);
			}

			yield return this.WaitForSyncUp();
			CardChoiceVisuals.instance.Hide();

			GameModeManager.TriggerHook(GameModeHooks.HookPickEnd);

			MapManager.instance.CallInNewMapAndMovePlayers(MapManager.instance.currentLevelID);
			TimeHandler.instance.DoSpeedUp();
			TimeHandler.instance.StartGame();
			GameManager.instance.battleOngoing = true;
			UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);
			PlayerManager.instance.InvokeMethod("SetPlayersVisible", true);

			this.StartCoroutine(this.DoRoundStart());
		}

		private IEnumerator RoundTransition(int winningTeamID) {
			yield return new WaitForSecondsRealtime(1f);
			MapManager.instance.LoadNextLevel(false, false);

			yield return new WaitForSecondsRealtime(1.3f);

			PlayerManager.instance.SetPlayersSimulated(false);
			TimeHandler.instance.DoSpeedUp();

			GameModeManager.TriggerHook(GameModeHooks.HookPickStart);

			PlayerManager.instance.InvokeMethod("SetPlayersVisible", false);
			var players = PlayerManager.instance.players;

			for (int i = 0; i < players.Count; i++) {
				if (players[i].teamID != winningTeamID) {
					yield return base.StartCoroutine(this.WaitForSyncUp());

					GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickStart);

					CardChoiceVisuals.instance.Show(i, true);
					yield return CardChoice.instance.DoPick(1, players[i].playerID, PickerType.Player);

					GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickEnd);

					yield return new WaitForSecondsRealtime(0.1f);
				}
			}

			PlayerManager.instance.InvokeMethod("SetPlayersVisible", true);

			GameModeManager.TriggerHook(GameModeHooks.HookPickEnd);

			yield return this.StartCoroutine(this.WaitForSyncUp());

			TimeHandler.instance.DoSlowDown();
			MapManager.instance.CallInNewMapAndMovePlayers(MapManager.instance.currentLevelID);
			PlayerManager.instance.RevivePlayers();

			yield return new WaitForSecondsRealtime(0.3f);

			TimeHandler.instance.DoSpeedUp();
			GameManager.instance.battleOngoing = true;
			this.isTransitioning = false;
			UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);

			this.StartCoroutine(this.DoRoundStart());
		}

		private IEnumerator PointTransition()
		{
			yield return new WaitForSecondsRealtime(1f);

			MapManager.instance.LoadNextLevel(false, false);

			yield return new WaitForSecondsRealtime(0.5f);
			yield return base.StartCoroutine(this.WaitForSyncUp());

			MapManager.instance.CallInNewMapAndMovePlayers(MapManager.instance.currentLevelID);
			PlayerManager.instance.RevivePlayers();

			yield return new WaitForSecondsRealtime(0.3f);

			TimeHandler.instance.DoSpeedUp();
			GameManager.instance.battleOngoing = true;
			this.isTransitioning = false;
			UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);

			this.StartCoroutine(this.DoPointStart());
		}

		private IEnumerator DoRoundStart() {
			// Wait for MapManager to set all players to simulated after map transition
			while (PlayerManager.instance.players.ToList().Any(p => !(bool) p.data.playerVel.GetFieldValue("simulated"))) {
				yield return null;
			}

			PlayerManager.instance.SetPlayersSimulated(false);

			GameModeManager.TriggerHook(GameModeHooks.HookRoundStart);
			GameModeManager.TriggerHook(GameModeHooks.HookPointStart);

			var sounds = GameObject.Find("/SonigonSoundEventPool");

			for (int i = 4; i >= 1; i--) {
				UIHandler.instance.DisplayRoundStartText($"{i}");
				SoundManager.Instance.Play(PointVisualizer.instance.sound_UI_Arms_Race_A_Ball_Shrink_Go_To_Left_Corner, this.transform);
				yield return new WaitForSeconds(0.5f);
			}

			SoundManager.Instance.Play(PointVisualizer.instance.sound_UI_Arms_Race_C_Ball_Pop_Shake, this.transform);
			UIHandler.instance.DisplayRoundStartText("FIGHT");
			PlayerManager.instance.SetPlayersSimulated(true);

			GameModeManager.TriggerHook(GameModeHooks.HookBattleStart);

			this.ExecuteAfterSeconds(1f, () => {
				UIHandler.instance.HideRoundStartText();
			});
		}

		private IEnumerator DoPointStart()
		{
			// Wait for MapManager to set all players to simulated after map transition
			while (PlayerManager.instance.players.ToList().Any(p => !(bool) p.data.playerVel.GetFieldValue("simulated")))
			{
				yield return null;
			}

			PlayerManager.instance.SetPlayersSimulated(false);

			GameModeManager.TriggerHook(GameModeHooks.HookPointStart);

			var sounds = GameObject.Find("/SonigonSoundEventPool");

			for (int i = 4; i >= 1; i--)
			{
				UIHandler.instance.DisplayRoundStartText($"{i}");
				SoundManager.Instance.Play(PointVisualizer.instance.sound_UI_Arms_Race_A_Ball_Shrink_Go_To_Left_Corner, this.transform);
				yield return new WaitForSeconds(0.5f);
			}

			SoundManager.Instance.Play(PointVisualizer.instance.sound_UI_Arms_Race_C_Ball_Pop_Shake, this.transform);
			UIHandler.instance.DisplayRoundStartText("FIGHT");
			PlayerManager.instance.SetPlayersSimulated(true);

			GameModeManager.TriggerHook(GameModeHooks.HookBattleStart);

			this.ExecuteAfterSeconds(1f, () => {
				UIHandler.instance.HideRoundStartText();
			});
		}

		private void RoundOver(int winningTeamID)
		{
			GameModeManager.TriggerHook(GameModeHooks.HookPointEnd);
			GameModeManager.TriggerHook(GameModeHooks.HookRoundEnd);

			this.currentWinningTeamID = winningTeamID;

			foreach (var teamID in this.teamPoints.Keys.ToList()) {
				this.teamPoints[teamID] = 0;
			}

			this.StartCoroutine(PointVisualizer.instance.DoWinSequence(this.teamPoints, this.teamRounds, winningTeamID));
			this.StartCoroutine(this.RoundTransition(winningTeamID));
		}

		private void PointOver(int winningTeamID)
		{
			GameModeManager.TriggerHook(GameModeHooks.HookPointEnd);

			this.currentWinningTeamID = winningTeamID;

			this.StartCoroutine(PointVisualizer.instance.DoSequence(this.teamPoints, this.teamRounds, winningTeamID));
			this.StartCoroutine(this.PointTransition());
		}

		private IEnumerator GameOverTransition(int winningTeamID)
		{
			GameModeManager.TriggerHook(GameModeHooks.HookGameEnd);

			UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);
			UIHandler.instance.DisplayScreenText(PlayerManager.instance.GetColorFromPlayer(winningTeamID).winText, "VICTORY!", 1f);
			yield return new WaitForSecondsRealtime(2f);
			this.GameOverRematch(winningTeamID);
			yield break;
		}

		private void GameOverRematch(int winningPlayerID) {
			if (PhotonNetwork.OfflineMode) {
				var winningPlayer = PlayerManager.instance.players.Find(p => p.playerID == winningPlayerID);
				UIHandler.instance.DisplayScreenTextLoop(PlayerManager.instance.GetColorFromPlayer(winningPlayerID).winText, "REMATCH?");
				UIHandler.instance.popUpHandler.StartPicking(winningPlayer, this.GetRematchYesNo);
				MapManager.instance.LoadNextLevel(false, false);
				return;
			}

			if (PhotonNetwork.IsMasterClient) {
				foreach (var player in PhotonNetwork.CurrentRoom.Players.Values.ToList()) {
					PhotonNetwork.DestroyPlayerObjects(player);
				}
			}

			SceneManager.LoadScene(SceneManager.GetActiveScene().name);
		}

		private void GetRematchYesNo(PopUpHandler.YesNo yesNo) {
			if (yesNo == PopUpHandler.YesNo.Yes) {
				base.StartCoroutine(this.IDoRematch());
				return;
			}
			this.DoRestart();
		}

		private IEnumerator IDoRematch() {
			yield return null;
			UIHandler.instance.StopScreenTextLoop();
			CardBarHandler.instance.ResetCardBards();

			PlayerManager.instance.InvokeMethod("ResetCharacters");

			this.ResetMatch();
			this.StartCoroutine(this.DoStartGame());
		}

		private void ResetMatch() {
			foreach (var player in PlayerManager.instance.players) {
				this.teamPoints[player.teamID] = 0;
				this.teamRounds[player.teamID] = 0;
				this.waitingForPlayer[player.playerID] = false;
			}

			this.isTransitioning = false;
			UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);
			CardBarHandler.instance.ResetCardBards();
			PointVisualizer.instance.ResetPoints();
		}

		public void Reset()
		{
			this.teamPoints.Clear();
			this.teamRounds.Clear();
			this.waitingForPlayer.Clear();
			this.isTransitioning = false;
		}

		private void DoRestart() {
			GameManager.instance.battleOngoing = false;
			if (PhotonNetwork.OfflineMode) {
				SceneManager.LoadScene(SceneManager.GetActiveScene().name);
				return;
			}
			NetworkConnectionHandler.instance.NetworkRestart();
		}

		private void GameOver(int winningPlayerID) {
			this.currentWinningTeamID = winningPlayerID;
			base.StartCoroutine(this.GameOverTransition(winningPlayerID));
		}

		[UnboundRPC]
		public static void RPCA_NextRound(int winningTeamID, Dictionary<int, int> teamPoints, Dictionary<int, int> teamRounds) {
			var instance = GM_Deathmatch.instance;

			if (instance.isTransitioning) {
				return;
			}

			GameManager.instance.battleOngoing = false;
			instance.teamPoints = teamPoints;
			instance.teamRounds = teamRounds;
			instance.isTransitioning = true;

			PlayerManager.instance.SetPlayersSimulated(false);

			instance.teamPoints[winningTeamID] = instance.teamPoints[winningTeamID] + 1;

			if (instance.teamPoints[winningTeamID] < (int) GameModeManager.CurrentHandler.Settings["pointsToWinRound"])
			{
				instance.PointOver(winningTeamID);
				return;
			}

			instance.teamRounds[winningTeamID] = instance.teamRounds[winningTeamID] + 1;

			if (instance.teamRounds[winningTeamID] >= (int) GameModeManager.CurrentHandler.Settings["roundsToWinGame"]) {
				instance.GameOver(winningTeamID);
				return;
			}

			instance.RoundOver(winningTeamID);
			return;
		}
	}
}