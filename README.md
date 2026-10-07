# Microservice Architecture Project

This project demonstrates a fully containerized microservice architecture built with Docker and Docker Compose.

## Architecture Overview

The system consists of several isolated services communicating across dedicated Docker networks:

- **Frontend**: A vanilla HTML/JS/CSS web application served via an Nginx web server. Nginx also acts as a reverse proxy to route `/api/` requests to the backend, seamlessly solving CORS issues.
- **Backend API**: An ASP.NET Core REST API that handles business logic and data persistence.
- **Database**: PostgreSQL database for persistent data storage, utilizing Docker volumes (`db_data`) to ensure data survives container restarts.
- **Caching**: Redis is used for high-performance data caching.
- **Message Broker**: RabbitMQ is integrated for event-driven communication and background task processing.

## Network Isolation

Security and isolation are strictly maintained using Docker networks:
- `frontend-net`: Contains the Frontend and API.
- `backend-net`: A secure internal network containing the API, PostgreSQL, Redis, and RabbitMQ. 
*Note: The frontend container has no direct access to the database or internal infrastructure.*

## Local Development Setup

For security reasons, the `.env` file containing secrets is not included in this repository. To run this project locally, you must create an `.env` file inside the `Service` directory before starting Docker Compose.

**1. Create the `.env` file:**
Navigate to the `Service` folder and create a file named `.env`.

**2. Add the required variables:**
Add the following lines to your new `.env` file (you can use these exact values or replace them with your own passwords):

```env
POSTGRES_PASSWORD=SecurePassword123!
RABBITMQ_USER=admin
RABBITMQ_PASSWORD=super_secret_rabbit
```

**3. Run the project:**
Once the `.env` file is ready, you can start all services and scale the API by running:
```bash
docker compose up -d --scale api=3
```
