using System;
using R3;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Tells the player when a save was rebuilt from its backup.
    //
    // The recovery itself has always worked; JsonSaveService.OnSaveRecoveredAsObservable has existed
    // since the backup system shipped and NOTHING subscribed to it. Silent recovery means a player
    // loses the last session's progress, sees no explanation, and reports it as a bug that cannot be
    // reproduced. One toast closes that.
    public sealed class SaveRecoveryNotifier : IStartable, IDisposable
    {
        private readonly ISaveRecoveryEvents _saves;
        private readonly INotificationService _notifications;
        private IDisposable _subscription;

        // ISaveRecoveryEvents, not the concrete JsonSaveService. Taking the concrete type looked
        // harmless — the scope registers it AsSelf — but a game that substitutes its own ISaveService
        // would leave this listening to a second JsonSaveService that nothing else uses, so the toast
        // would simply never fire and nothing would say why.
        [Inject]
        public SaveRecoveryNotifier(ISaveRecoveryEvents saves, INotificationService notifications)
        {
            _saves = saves;
            _notifications = notifications;
        }

        public void Start()
        {
            // ObserveOnMainThread is a seatbelt, not a formality. Recovery runs inside a load, the
            // storage backend does its I/O on a thread pool on every platform except WebGL, and
            // INotificationService touches Unity objects — one unlucky continuation without this
            // would throw from a background thread, where the failure is far harder to read.
            _subscription = _saves.OnSaveRecoveredAsObservable
                .ObserveOnMainThread()
                .Subscribe(OnRecovered);
        }

        private void OnRecovered(SaveRecoveredEventArgs args)
        {
            Debug.LogWarning($"[SaveRecovery] '{args.Key}' was restored from its backup " +
                             $"(primary repaired: {args.PrimaryRepaired}). Progress since the " +
                             "previous save is gone.");

            // Keyed by the save key so a game that recovers several keys at boot gets one toast per
            // key rather than one stacked pile, and a repeat for the same key merges.
            _notifications.Notify(new NotificationRequest(
                "save", args.Key,
                new NotificationContent(
                    title: "Progress restored",
                    body: "Your last save could not be read, so a backup was used. " +
                          "Some recent progress may be missing.")));
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
