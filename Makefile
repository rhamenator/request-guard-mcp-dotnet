.PHONY: restore build test fmt format audit ci run docker-build docker-run docker-compose-up k8s-apply clean

SOLUTION := RequestGuardMcp.slnx
HOST_PROJECT := src/RequestGuardMcp.Host/RequestGuardMcp.Host.csproj

restore:
	dotnet restore $(SOLUTION) --locked-mode

build: restore
	dotnet build $(SOLUTION) --configuration Release --no-restore --warnaserror

test: build
	dotnet test $(SOLUTION) --configuration Release --no-build

fmt:
	dotnet format $(SOLUTION)

format: fmt

format-check:
	dotnet format $(SOLUTION) --verify-no-changes

audit: restore
	dotnet list $(SOLUTION) package --vulnerable --include-transitive

ci: format-check build test audit

run:
	dotnet run --project $(HOST_PROJECT)

docker-build:
	docker build -t request-guard-mcp-dotnet:latest -f docker/Dockerfile .

docker-run:
	docker run --rm -p 8085:8085 --env-file .env request-guard-mcp-dotnet:latest

docker-compose-up:
	docker compose -f docker/docker-compose.yml up

k8s-apply:
	kubectl apply -f deploy/k8s/configmap.yaml
	kubectl apply -f deploy/k8s/backends.yaml
	kubectl apply -f deploy/k8s/deployment.yaml
	kubectl apply -f deploy/k8s/service.yaml
	kubectl apply -f deploy/k8s/hpa.yaml
	kubectl apply -f deploy/k8s/networkpolicy.yaml

clean:
	dotnet clean $(SOLUTION)
	find . -type d \( -name bin -o -name obj \) -not -path "./.git/*" -exec rm -rf {} +
