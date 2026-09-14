using ExitGames.Client.Photon;
using MoreStandsForShops.Shop;
using Photon.Pun;
using Photon.Realtime;
using System;
using UnityEngine;

namespace MoreStandsForShops.Network;

/// <summary>
/// Provides authority-transfer recovery independently of the optional upgrade stand.
/// </summary>
internal sealed class ShopTableStabilizationCallbacks : MonoBehaviour, IInRoomCallbacks
{
    private static ShopTableStabilizationCallbacks _instance;
    private bool _registered;

    internal static void BeginShopSession()
    {
        if (Plugin.Instance == null)
            return;

        if (_instance == null)
        {
            _instance = Plugin.Instance.GetComponent<ShopTableStabilizationCallbacks>();
            if (_instance == null)
                _instance = Plugin.Instance.gameObject.AddComponent<ShopTableStabilizationCallbacks>();
        }

        _instance.Register();
    }

    internal static void EndShopSession()
    {
        if (_instance != null)
            _instance.Unregister();
    }

    private void Register()
    {
        if (_registered)
            return;

        PhotonNetwork.AddCallbackTarget(this);
        _registered = true;
    }

    private void Unregister()
    {
        if (!_registered)
            return;

        PhotonNetwork.RemoveCallbackTarget(this);
        _registered = false;
    }

    private void OnDisable()
    {
        Unregister();
    }

    private void OnDestroy()
    {
        Unregister();
        if (_instance == this)
            _instance = null;
    }

    public void OnMasterClientSwitched(Player newMasterClient)
    {
        // This callback is registered only while a shop scene is active. The room
        // property is an additional guard against an early/late Photon callback.
        if (!PhotonNetwork.InRoom || !ShopLayoutSync.HasTableStabilizedViewIds())
            return;

        try
        {
            ShopTableItemPlacementController.HandleMasterClientSwitched();
        }
        catch (Exception ex)
        {
            // Never let an optional display-stability recovery abort Photon room flow.
            Plugin.Log.LogError(
                $"[TableItemStabilizer] Host-migration recovery callback failed; " +
                $"continuing the room without recovery.\n{ex}");
        }
    }

    public void OnPlayerEnteredRoom(Player newPlayer)
    {
    }

    public void OnPlayerLeftRoom(Player otherPlayer)
    {
    }

    public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
    }

    public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
    }
}
