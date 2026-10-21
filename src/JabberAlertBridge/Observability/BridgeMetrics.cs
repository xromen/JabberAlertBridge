using Prometheus;

namespace JabberAlertBridge.Observability;

public sealed class BridgeMetrics
{
    public Counter Webhooks { get; } = Metrics.CreateCounter(
        "jabber_alert_webhooks_received_total",
        "Webhook requests by source and HTTP status.",
        new CounterConfiguration { LabelNames = ["source", "status"] });

    public Counter Enqueued { get; } = Metrics.CreateCounter(
        "jabber_alert_messages_enqueued_total",
        "Per-recipient messages committed to the queue.");

    public Counter Sent { get; } = Metrics.CreateCounter(
        "jabber_alert_messages_sent_total",
        "Direct messages sent using the configured acknowledgement policy.");

    public Counter Failures { get; } = Metrics.CreateCounter(
        "jabber_alert_message_send_failures_total",
        "Failed send attempts.");

    public Gauge Pending { get; } = Metrics.CreateGauge(
        "jabber_alert_queue_pending",
        "Pending and processing per-recipient messages.");

    public Gauge OldestAge { get; } = Metrics.CreateGauge(
        "jabber_alert_queue_oldest_age_seconds",
        "Age of the oldest active message.");

    public Gauge XmppReady { get; } = Metrics.CreateGauge(
        "jabber_alert_xmpp_ready",
        "XMPP authenticated and ready for direct messages.");

    public Gauge QueueCapacity { get; } = Metrics.CreateGauge(
        "jabber_alert_queue_capacity",
        "Maximum active per-recipient messages.");

    public Gauge DatabaseReady { get; } = Metrics.CreateGauge(
        "jabber_alert_database_ready",
        "Last successful queue snapshot.");

    public Gauge Dead { get; } = Metrics.CreateGauge(
        "jabber_alert_queue_dead",
        "Messages requiring manual intervention.");

    public Histogram SendDuration { get; } = Metrics.CreateHistogram(
        "jabber_alert_send_duration_seconds",
        "Direct message send time including server acknowledgement when enabled.");
}
