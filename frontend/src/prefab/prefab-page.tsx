import React, {useEffect, useState} from "react";
import {getAll, PrefabItem} from "./prefab-service"
import {Button, Paper, Stack} from "@mui/material";
import {DataGrid, GridColDef} from "@mui/x-data-grid";
import DashboardContainer from "../dashboard/dashboard-container";

interface PrefabPageProps {}

const PrefabPage: React.FC<PrefabPageProps> = () => {

    const [data, setData] = useState<PrefabItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);

    const fetchData = async () => {
        setLoading(true);
        const response = await getAll();
        setLoading(false);
        setData(response);
    };

    const columns: GridColDef[] = [
        {field: 'name', headerName: 'Name', minWidth: 250, },
        {field: 'folder', headerName: 'Folder'},
        {field: 'path', headerName: 'Blueprint', minWidth: 250},
        {field: 'serverProperties.isDynamicWreck', headerName: 'Wreck', minWidth: 250},
    ];

    const paginationModel = { page: 0, pageSize: 10 };

    useEffect(() => {
        fetchData();
    }, []);

    return (
        <DashboardContainer title="Prefabs">
            <Stack spacing={2} direction="row">
                <Button variant="contained">Add</Button>
            </Stack>
            <br />
            <Paper>
                <DataGrid
                    rows={data}
                    columns={columns}
                    initialState={{ pagination: { paginationModel } }}
                    pageSizeOptions={[10, 20, 30, 40, 50, 100]}
                    checkboxSelection
                    sx={{ border: 0 }}
                />
            </Paper>
        </DashboardContainer>
    );
}

export default PrefabPage;