using Prometheus;

namespace JabberAlertBridge.Observability;

public sealed class BridgeMetrics
{
    public Counter Webhooks { get; } = Metrics.CreateCounter("jabber_alert_webhooks_received_total",
        "Webhook requests by source and HTTP status.", new CounterConfiguration { LabelNames = ["source", "status"] });
    public Counter Enqueued { get; } = Metrics.CreateCounter("jabber_alert_messages_enqueued_total", "Messages committed to the queue.");
    public Counter Sent { get; } = Metrics.CreateCounter("jabber_alert_messages_sent_total", "Messages confirmed by MUC echo.");
    public Counter Failures { get; } = Metrics.CreateCounter("jabber_alert_message_send_failures_total", "Failed send attempts.");
    public Gauge Pending { get; } = Metrics.CreateGauge("jabber_alert_queue_pending", "Pending and processing messages.");
    public Gauge OldestAge { get; } = Metrics.CreateGauge("jabber_alert_queue_oldest_age_seconds", "Age of the oldest active message.");
    public Gauge XmppReady { get; } = Metrics.CreateGauge("jabber_alert_xmpp_ready", "Bot authenticated and present in the room.");
    public Gauge QueueCapacity { get; } = Metrics.CreateGauge("jabber_alert_queue_capacity", "Maximum active messages.");
    public Gauge DatabaseReady { get; } = Metrics.CreateGauge("jabber_alert_database_ready", "Last successful queue snapshot.");
    public Gauge Dead { get; } = Metrics.CreateGauge("jabber_alert_queue_dead", "Messages requiring manual intervention.");
    public Histogram SendDuration { get; } = Metrics.CreateHistogram("jabber_alert_send_duration_seconds", "Send time including MUC echo.");
}
