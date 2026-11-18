-- CropPilot current PostgreSQL 18 schema.
-- Source: EF Core migrations through 20261118023002_AddAuditTimestamps.
-- Regenerate after every schema migration.

--
-- PostgreSQL database dump
--

\restrict CropPilotSchemaSnapshot

-- Dumped from database version 18.6 (Debian 18.6-1.pgdg13+2)
-- Dumped by pg_dump version 18.6 (Debian 18.6-1.pgdg13+2)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: agricultural_materials; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.agricultural_materials (
    id uuid NOT NULL,
    main_account_id uuid NOT NULL,
    name character varying(100) NOT NULL COLLATE pg_catalog."C",
    material_type character varying(24) NOT NULL,
    stock_unit character varying(20) NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    package_quantity numeric(15,6),
    package_unit character varying(10),
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT ck_agricultural_materials_name CHECK ((btrim((name)::text) <> ''::text)),
    CONSTRAINT ck_agricultural_materials_package CHECK ((((package_quantity IS NULL) AND (package_unit IS NULL)) OR ((package_quantity IS NOT NULL) AND ((package_quantity >= 0.000001) AND (package_quantity <= 999999999.999999)) AND (package_unit IS NOT NULL) AND ((package_unit)::text = ANY ((ARRAY['LITER'::character varying, 'MILLILITER'::character varying, 'KILOGRAM'::character varying, 'GRAM'::character varying])::text[]))))),
    CONSTRAINT ck_agricultural_materials_stock_unit CHECK (((btrim((stock_unit)::text) <> ''::text) AND (((material_type)::text <> 'PESTICIDE'::text) OR ((stock_unit)::text = ANY ((ARRAY['LITER'::character varying, 'MILLILITER'::character varying, 'KILOGRAM'::character varying, 'GRAM'::character varying])::text[]))))),
    CONSTRAINT ck_agricultural_materials_type CHECK (((material_type)::text = ANY ((ARRAY['FERTILIZER'::character varying, 'PESTICIDE'::character varying])::text[])))
);


--
-- Name: auth_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.auth_sessions (
    id uuid NOT NULL,
    main_account_id uuid NOT NULL,
    security_stamp character varying(256),
    expires_at timestamp with time zone NOT NULL
);


--
-- Name: ef_migrations_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ef_migrations_history (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL
);


--
-- Name: federated_identities; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.federated_identities (
    id uuid NOT NULL,
    main_account_id uuid NOT NULL,
    identity_provider character varying(32) NOT NULL,
    provider_user_id character varying(255) NOT NULL,
    CONSTRAINT ck_federated_identities_provider CHECK (((identity_provider)::text = 'GOOGLE'::text))
);


--
-- Name: field_operations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.field_operations (
    id uuid NOT NULL,
    planting_id uuid NOT NULL,
    operation_type character varying(24) NOT NULL,
    operation_date date NOT NULL,
    description character varying(500),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT ck_field_operations_type CHECK (((operation_type)::text = ANY ((ARRAY['MATERIAL_APPLICATION'::character varying, 'IRRIGATION'::character varying])::text[])))
);


--
-- Name: fields; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.fields (
    id uuid NOT NULL,
    main_account_id uuid NOT NULL,
    name character varying(120) NOT NULL COLLATE pg_catalog."C",
    area_square_meters numeric(14,4) NOT NULL,
    latitude numeric(9,6),
    longitude numeric(9,6),
    description character varying(1000),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT ck_fields_area_square_meters CHECK (((area_square_meters > (0)::numeric) AND (area_square_meters <= 9999999999.9999))),
    CONSTRAINT ck_fields_coordinates CHECK ((((latitude IS NULL) AND (longitude IS NULL)) OR ((latitude IS NOT NULL) AND (longitude IS NOT NULL) AND ((latitude >= ('-90'::integer)::numeric) AND (latitude <= (90)::numeric)) AND ((longitude >= ('-180'::integer)::numeric) AND (longitude <= (180)::numeric))))),
    CONSTRAINT ck_fields_name CHECK ((btrim((name)::text, U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000'::text) <> ''::text))
);


--
-- Name: harvests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.harvests (
    id uuid NOT NULL,
    planting_id uuid NOT NULL,
    harvest_date date NOT NULL,
    weight_kg numeric(15,6) NOT NULL,
    description character varying(500),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT ck_harvests_weight_kg CHECK (((weight_kg > (0)::numeric) AND (weight_kg < (1000000000)::numeric)))
);


--
-- Name: irrigation_details; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.irrigation_details (
    id uuid NOT NULL,
    field_operation_id uuid NOT NULL,
    method character varying(24) NOT NULL,
    water_volume_liters numeric(15,6),
    operation_type character varying(24) NOT NULL,
    CONSTRAINT ck_irrigation_details_method CHECK (((method)::text = ANY ((ARRAY['DRIP'::character varying, 'SPRINKLER'::character varying, 'FURROW'::character varying, 'MANUAL'::character varying, 'OTHER'::character varying])::text[]))),
    CONSTRAINT ck_irrigation_details_operation_type CHECK (((operation_type)::text = 'IRRIGATION'::text)),
    CONSTRAINT ck_irrigation_details_water_volume_liters CHECK (((water_volume_liters > (0)::numeric) AND (water_volume_liters < (1000000000)::numeric)))
);


--
-- Name: local_credentials; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.local_credentials (
    id uuid NOT NULL,
    main_account_id uuid NOT NULL,
    username character varying(256) NOT NULL,
    password_hash character varying(512) NOT NULL,
    access_failed_count integer NOT NULL,
    lockout_enabled boolean NOT NULL,
    lockout_end timestamp with time zone,
    concurrency_stamp uuid NOT NULL,
    CONSTRAINT ck_local_credentials_username CHECK ((length((username)::text) > 0))
);


--
-- Name: main_account; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.main_account (
    id uuid CONSTRAINT asp_net_users_id_not_null NOT NULL,
    security_stamp character varying(256),
    status character varying(32) DEFAULT 'PENDING'::character varying NOT NULL,
    CONSTRAINT ck_main_account_status CHECK (((status)::text = ANY ((ARRAY['PENDING'::character varying, 'ACTIVE'::character varying, 'DISABLED'::character varying])::text[])))
);


--
-- Name: material_application_details; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.material_application_details (
    id uuid NOT NULL,
    material_application_id uuid NOT NULL,
    material_purchase_lot_id uuid NOT NULL,
    quantity numeric(15,6) NOT NULL,
    sort_order integer NOT NULL,
    CONSTRAINT ck_material_application_details_quantity CHECK (((quantity >= 0.000001) AND (quantity <= 999999999.999999))),
    CONSTRAINT ck_material_application_details_sort_order CHECK ((sort_order >= 0))
);


--
-- Name: material_applications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.material_applications (
    id uuid NOT NULL,
    field_operation_id uuid NOT NULL,
    spray_water_volume_liters numeric(15,6),
    application_method character varying(24) NOT NULL,
    operation_type character varying(24) NOT NULL,
    CONSTRAINT ck_material_applications_method CHECK (((application_method)::text = ANY ((ARRAY['SPRAY'::character varying, 'BROADCAST'::character varying])::text[]))),
    CONSTRAINT ck_material_applications_operation_type CHECK (((operation_type)::text = 'MATERIAL_APPLICATION'::text)),
    CONSTRAINT ck_material_applications_water CHECK (((spray_water_volume_liters IS NULL) OR (((application_method)::text = 'SPRAY'::text) AND ((spray_water_volume_liters >= 0.000001) AND (spray_water_volume_liters <= 999999999.999999)))))
);


--
-- Name: material_purchase_lots; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.material_purchase_lots (
    id uuid NOT NULL,
    material_id uuid NOT NULL,
    source character varying(24) NOT NULL,
    purchased_on date,
    available_from date NOT NULL,
    received_quantity numeric(15,6) NOT NULL,
    unit_price numeric(15,6),
    currency_code character(3) NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT ck_material_purchase_lots_currency CHECK ((currency_code = 'TWD'::bpchar)),
    CONSTRAINT ck_material_purchase_lots_dates CHECK (((((source)::text = 'PURCHASE'::text) AND (purchased_on IS NOT NULL) AND (purchased_on = available_from)) OR (((source)::text = 'OPENING_STOCK'::text) AND ((purchased_on IS NULL) OR (purchased_on <= available_from))))),
    CONSTRAINT ck_material_purchase_lots_price CHECK (((unit_price >= (0)::numeric) AND (unit_price <= 999999999.999999))),
    CONSTRAINT ck_material_purchase_lots_quantity CHECK (((received_quantity >= 0.000001) AND (received_quantity <= 999999999.999999)))
);


--
-- Name: plantings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.plantings (
    id uuid NOT NULL,
    field_id uuid NOT NULL,
    crop_name character varying(120) NOT NULL,
    variety character varying(120),
    planned_planting_date date,
    actual_planting_date date,
    status character varying(16) DEFAULT 'PLANNED'::character varying NOT NULL,
    description character varying(1000),
    area_square_meters numeric(14,4) NOT NULL,
    completion_date date,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT ck_plantings_actual_planting_date CHECK ((((status)::text <> ALL ((ARRAY['GROWING'::character varying, 'COMPLETED'::character varying])::text[])) OR (actual_planting_date IS NOT NULL))),
    CONSTRAINT ck_plantings_area_square_meters CHECK ((area_square_meters > (0)::numeric)),
    CONSTRAINT ck_plantings_completion_date CHECK (((((status)::text = 'COMPLETED'::text) = (completion_date IS NOT NULL)) AND (completion_date >= actual_planting_date))),
    CONSTRAINT ck_plantings_crop_name CHECK ((btrim((crop_name)::text, U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000'::text) <> ''::text)),
    CONSTRAINT ck_plantings_status CHECK (((status)::text = ANY ((ARRAY['PLANNED'::character varying, 'GROWING'::character varying, 'HARVESTING'::character varying, 'COMPLETED'::character varying])::text[])))
);


--
-- Name: user_profiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_profiles (
    id uuid NOT NULL,
    main_account_id uuid NOT NULL,
    email character varying(256),
    display_name character varying(120),
    surname character varying(120),
    given_name character varying(120)
);


--
-- Name: user_roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_roles (
    id uuid NOT NULL,
    main_account_id uuid NOT NULL,
    role character varying(16) NOT NULL,
    CONSTRAINT ck_user_roles_role CHECK (((role)::text = ANY ((ARRAY['USER'::character varying, 'ADMIN'::character varying])::text[])))
);


--
-- Name: field_operations ak_field_operations_id_operation_type; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.field_operations
    ADD CONSTRAINT ak_field_operations_id_operation_type UNIQUE (id, operation_type);


--
-- Name: agricultural_materials pk_agricultural_materials; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.agricultural_materials
    ADD CONSTRAINT pk_agricultural_materials PRIMARY KEY (id);


--
-- Name: auth_sessions pk_auth_sessions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auth_sessions
    ADD CONSTRAINT pk_auth_sessions PRIMARY KEY (id);


--
-- Name: ef_migrations_history pk_ef_migrations_history; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ef_migrations_history
    ADD CONSTRAINT pk_ef_migrations_history PRIMARY KEY (migration_id);


--
-- Name: federated_identities pk_federated_identities; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.federated_identities
    ADD CONSTRAINT pk_federated_identities PRIMARY KEY (id);


--
-- Name: field_operations pk_field_operations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.field_operations
    ADD CONSTRAINT pk_field_operations PRIMARY KEY (id);


--
-- Name: fields pk_fields; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.fields
    ADD CONSTRAINT pk_fields PRIMARY KEY (id);


--
-- Name: harvests pk_harvests; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.harvests
    ADD CONSTRAINT pk_harvests PRIMARY KEY (id);


--
-- Name: irrigation_details pk_irrigation_details; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.irrigation_details
    ADD CONSTRAINT pk_irrigation_details PRIMARY KEY (id);


--
-- Name: local_credentials pk_local_credentials; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.local_credentials
    ADD CONSTRAINT pk_local_credentials PRIMARY KEY (id);


--
-- Name: main_account pk_main_account; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.main_account
    ADD CONSTRAINT pk_main_account PRIMARY KEY (id);


--
-- Name: material_application_details pk_material_application_details; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.material_application_details
    ADD CONSTRAINT pk_material_application_details PRIMARY KEY (id);


--
-- Name: material_applications pk_material_applications; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.material_applications
    ADD CONSTRAINT pk_material_applications PRIMARY KEY (id);


--
-- Name: material_purchase_lots pk_material_purchase_lots; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.material_purchase_lots
    ADD CONSTRAINT pk_material_purchase_lots PRIMARY KEY (id);


--
-- Name: plantings pk_plantings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.plantings
    ADD CONSTRAINT pk_plantings PRIMARY KEY (id);


--
-- Name: user_profiles pk_user_profiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_profiles
    ADD CONSTRAINT pk_user_profiles PRIMARY KEY (id);


--
-- Name: user_roles pk_user_roles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT pk_user_roles PRIMARY KEY (id);


--
-- Name: ix_auth_sessions_main_account_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_auth_sessions_main_account_id ON public.auth_sessions USING btree (main_account_id);


--
-- Name: ix_federated_identities_identity_provider_provider_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_federated_identities_identity_provider_provider_user_id ON public.federated_identities USING btree (identity_provider, provider_user_id);


--
-- Name: ix_federated_identities_main_account_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_federated_identities_main_account_id ON public.federated_identities USING btree (main_account_id);


--
-- Name: ix_field_operations_planting_id_operation_type_operation_date_; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_field_operations_planting_id_operation_type_operation_date_ ON public.field_operations USING btree (planting_id, operation_type, operation_date DESC, created_at DESC, id DESC);


--
-- Name: ix_fields_main_account_id_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_fields_main_account_id_name ON public.fields USING btree (main_account_id, name);


--
-- Name: ix_harvests_planting_id_harvest_date_created_at_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_harvests_planting_id_harvest_date_created_at_id ON public.harvests USING btree (planting_id, harvest_date DESC, created_at DESC, id DESC);


--
-- Name: ix_irrigation_details_field_operation_id_operation_type; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_irrigation_details_field_operation_id_operation_type ON public.irrigation_details USING btree (field_operation_id, operation_type);


--
-- Name: ix_local_credentials_main_account_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_local_credentials_main_account_id ON public.local_credentials USING btree (main_account_id);


--
-- Name: ix_material_application_details_material_application_id_sort_o; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_material_application_details_material_application_id_sort_o ON public.material_application_details USING btree (material_application_id, sort_order);


--
-- Name: ix_material_application_details_material_purchase_lot_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_material_application_details_material_purchase_lot_id ON public.material_application_details USING btree (material_purchase_lot_id);


--
-- Name: ix_material_applications_field_operation_id_operation_type; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_material_applications_field_operation_id_operation_type ON public.material_applications USING btree (field_operation_id, operation_type);


--
-- Name: ix_material_purchase_lots_material_id_available_from_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_material_purchase_lots_material_id_available_from_id ON public.material_purchase_lots USING btree (material_id, available_from, id);


--
-- Name: ix_plantings_field_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_plantings_field_id ON public.plantings USING btree (field_id);


--
-- Name: ix_user_profiles_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_user_profiles_email ON public.user_profiles USING btree (email);


--
-- Name: ix_user_profiles_main_account_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_user_profiles_main_account_id ON public.user_profiles USING btree (main_account_id);


--
-- Name: ix_user_roles_main_account_id_role; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_user_roles_main_account_id_role ON public.user_roles USING btree (main_account_id, role);


--
-- Name: ux_agricultural_materials_account_type_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_agricultural_materials_account_type_name ON public.agricultural_materials USING btree (main_account_id, material_type, name) WHERE (package_quantity IS NULL);


--
-- Name: ux_agricultural_materials_account_type_name_package; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_agricultural_materials_account_type_name_package ON public.agricultural_materials USING btree (main_account_id, material_type, name, package_quantity, package_unit) WHERE (package_quantity IS NOT NULL);


--
-- Name: ux_local_credentials_lower_username; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_local_credentials_lower_username ON public.local_credentials USING btree (lower((username)::text));


--
-- Name: agricultural_materials fk_agricultural_materials_main_accounts_main_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.agricultural_materials
    ADD CONSTRAINT fk_agricultural_materials_main_accounts_main_account_id FOREIGN KEY (main_account_id) REFERENCES public.main_account(id) ON DELETE RESTRICT;


--
-- Name: auth_sessions fk_auth_sessions_main_accounts_main_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auth_sessions
    ADD CONSTRAINT fk_auth_sessions_main_accounts_main_account_id FOREIGN KEY (main_account_id) REFERENCES public.main_account(id) ON DELETE CASCADE;


--
-- Name: federated_identities fk_federated_identities_main_accounts_main_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.federated_identities
    ADD CONSTRAINT fk_federated_identities_main_accounts_main_account_id FOREIGN KEY (main_account_id) REFERENCES public.main_account(id) ON DELETE CASCADE;


--
-- Name: field_operations fk_field_operations_plantings_planting_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.field_operations
    ADD CONSTRAINT fk_field_operations_plantings_planting_id FOREIGN KEY (planting_id) REFERENCES public.plantings(id) ON DELETE RESTRICT;


--
-- Name: fields fk_fields_main_accounts_main_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.fields
    ADD CONSTRAINT fk_fields_main_accounts_main_account_id FOREIGN KEY (main_account_id) REFERENCES public.main_account(id) ON DELETE RESTRICT;


--
-- Name: harvests fk_harvests_plantings_planting_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.harvests
    ADD CONSTRAINT fk_harvests_plantings_planting_id FOREIGN KEY (planting_id) REFERENCES public.plantings(id) ON DELETE RESTRICT;


--
-- Name: irrigation_details fk_irrigation_details_field_operations; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.irrigation_details
    ADD CONSTRAINT fk_irrigation_details_field_operations FOREIGN KEY (field_operation_id, operation_type) REFERENCES public.field_operations(id, operation_type) ON DELETE CASCADE;


--
-- Name: local_credentials fk_local_credentials_main_accounts_main_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.local_credentials
    ADD CONSTRAINT fk_local_credentials_main_accounts_main_account_id FOREIGN KEY (main_account_id) REFERENCES public.main_account(id) ON DELETE CASCADE;


--
-- Name: material_application_details fk_material_application_details_material_application_material_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.material_application_details
    ADD CONSTRAINT fk_material_application_details_material_application_material_ FOREIGN KEY (material_application_id) REFERENCES public.material_applications(id) ON DELETE CASCADE;


--
-- Name: material_application_details fk_material_application_details_material_purchase_lots_materia; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.material_application_details
    ADD CONSTRAINT fk_material_application_details_material_purchase_lots_materia FOREIGN KEY (material_purchase_lot_id) REFERENCES public.material_purchase_lots(id) ON DELETE RESTRICT;


--
-- Name: material_applications fk_material_applications_field_operations; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.material_applications
    ADD CONSTRAINT fk_material_applications_field_operations FOREIGN KEY (field_operation_id, operation_type) REFERENCES public.field_operations(id, operation_type) ON DELETE CASCADE;


--
-- Name: material_purchase_lots fk_material_purchase_lots_agricultural_materials_material_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.material_purchase_lots
    ADD CONSTRAINT fk_material_purchase_lots_agricultural_materials_material_id FOREIGN KEY (material_id) REFERENCES public.agricultural_materials(id) ON DELETE RESTRICT;


--
-- Name: plantings fk_plantings_fields_field_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.plantings
    ADD CONSTRAINT fk_plantings_fields_field_id FOREIGN KEY (field_id) REFERENCES public.fields(id) ON DELETE RESTRICT;


--
-- Name: user_profiles fk_user_profiles_main_accounts_main_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_profiles
    ADD CONSTRAINT fk_user_profiles_main_accounts_main_account_id FOREIGN KEY (main_account_id) REFERENCES public.main_account(id) ON DELETE CASCADE;


--
-- Name: user_roles fk_user_roles_main_account_main_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT fk_user_roles_main_account_main_account_id FOREIGN KEY (main_account_id) REFERENCES public.main_account(id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict CropPilotSchemaSnapshot


-- Record the EF Core migrations already represented by this snapshot.
INSERT INTO public.ef_migrations_history (migration_id, product_version) VALUES
    ('20261109111434_InitialIdentity', '10.0.13'),
    ('20261109113005_StandardizeAccountKeys', '10.0.13'),
    ('20261109142723_DesktopAccountModel', '10.0.13'),
    ('20261110074529_BearerAuthSessions', '10.0.13'),
    ('20261110085700_CreateFields', '10.0.13'),
    ('20261111023529_CreatePlantings', '10.0.13'),
    ('20261111071814_AddPlantingArea', '10.0.13'),
    ('20261111131045_AddFertilizationInventory', '10.0.13'),
    ('20261112000309_AddIrrigationRecords', '10.0.13'),
    ('20261112085516_ReplaceFertilizationWithMaterialApplications', '10.0.13'),
    ('20261115092315_AddMaterialPackageSpecifications', '10.0.13'),
    ('20261116073756_MoveMaterialApplicationMethod', '10.0.13'),
    ('20261117045413_CreateHarvests', '10.0.13'),
    ('20261118014141_DeriveApplicationCostsFromLots', '10.0.13'),
    ('20261118014227_AddPlantingCompletionDate', '10.0.13'),
    ('20261118022654_NormalizeEnumCodes', '10.0.13'),
    ('20261118022829_EnforceOperationDetailTypes', '10.0.13'),
    ('20261118023002_AddAuditTimestamps', '10.0.13');
