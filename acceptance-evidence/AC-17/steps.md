1. Prepare a ranked application with an accepted offer (the hire path since PLAN-27).
2. Stop the acceptance RabbitMQ container.
3. Complete the hire transaction through the gateway while RabbitMQ is unavailable.
4. Prove HR.Core committed one or more MassTransit outbox rows.
5. Restart RabbitMQ, prove the outbox drains, and prove Auth.Directory receives the employee.