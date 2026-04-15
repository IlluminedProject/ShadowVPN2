# ShadowVPN2: Multi-node VPN/Proxy Management System

## Core Concept
ShadowVPN2 is a distributed control panel for VPN and proxy services, designed to unify independent nodes into a single, fault-tolerant cluster. The entire system is delivered as a "batteries-included" single Docker container.

## Architectural Principles
1.  **High Availability (Shared Nothing):** Every node runs an **Embedded RavenDB** instance. These instances form a cluster, replicating all data (users, configs, rules) across all nodes. The loss of multiple nodes does not result in data loss or system downtime.
2.  **Automated Leadership:** The cluster automatically elects a leader for orchestrating global tasks, while each node remains capable of serving clients autonomously.
3.  **Modular Engine:** A plugin-based architecture for VPN/Proxy backends. Initial support includes **sing-box** (universal proxy engine) and **AmneziaVPN**.
4.  **Dynamic Overlay Network:** Nodes are interconnected via a dynamic L3 overlay (e.g., Wireguard/p2p). This enables seamless traffic routing between nodes and provides connectivity between all clients connected to the same cluster.
5.  **Single Container Deployment:** The web panel, database, and VPN engines all ship within one Docker image for simplified deployment and scaling.

## Key Features
-   **Advanced Traffic Orchestration:**
    -   Route specific traffic (by domain, IP, or geo) through designated "Exit Nodes".  
        Example: Access RU-resources via a Russian node, while gaming traffic goes through a low-latency node, regardless of which entry point the user connected to.
    -   Automatic failover for exit routes.
-   **Identity & Access:**
    -   Built-in **OpenIDDict** server for robust authentication.
    -   Dynamic OAuth provider management via the UI without restarts.
    -   Self-service portal for users to manage their own devices and configurations.
-   **Setup Wizard:** A web-based first-run experience to configure the cluster, generate Docker configs, and initialize the first node.

## Technical Stack
-   **Framework:** .NET 10 (Blazor WebApp, Interactive Server mode)
-   **Database:** RavenDB (Embedded + Clustering)
-   **Security:** ASP.NET Core Identity + OpenIDDict
-   **Core Engine:** sing-box
-   **Networking:** Dynamic Overlay (Wireguard-based)
-   **OS/Env:** Linux-based Docker Container
